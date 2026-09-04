using EDSC.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// Sends each head pose either to Opentrack over UDP (default) or straight into the
    /// FreeTrack shared memory that the game's NPClient DLL reads (direct mode).
    ///
    /// Opentrack does its own centring and axis mapping. In direct mode this router
    /// subtracts a captured centre pose so the game sees zero when you look straight ahead.
    /// </summary>
    public sealed class PoseOutputRouter : IDisposable
    {
        private readonly object _lock = new object();
        private readonly OpentrackUdpSender? _udpSender;
        private readonly FreeTrackSharedMemorySender _freeTrackSender = new FreeTrackSharedMemorySender();

        private bool _directOutputEnabled;
        private bool _centerPending;
        private HeadPose? _center;
        private double _smoothingStrength = 0.5;

        // Centring waits for the user to settle (they are often still holding the phone when tracking
        // starts) and then averages a short window rather than trusting a single pose.
        private const double CentreSettleSeconds = 3.0;
        private const double CentreAverageSeconds = 1.0;
        private double _centreRequestedAt = -1.0;
        private readonly System.Collections.Generic.List<HeadPose> _centreSamples = new System.Collections.Generic.List<HeadPose>();

        // Opentrack normally filters the pose; in direct mode this filter takes that job.
        // One filter per axis: X, Y, Z, Yaw, Pitch, Roll.
        private readonly OneEuroFilter[] _filters = new OneEuroFilter[6];
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public PoseOutputRouter(OpentrackUdpSender? udpSender)
        {
            _udpSender = udpSender;

            for (int i = 0; i < _filters.Length; i++)
            {
                _filters[i] = new OneEuroFilter();
            }

            ApplySmoothing();
        }

        /// <summary>Sensitivity scales. All poses reach the router unscaled; gain is applied here, after centring.</summary>
        public double TranslationScale { get; set; } = 1.0;
        public double YawScale { get; set; } = 1.0;
        public double PitchScale { get; set; } = 1.0;
        public double RollScale { get; set; } = 1.0;

        /// <summary>
        /// Apply the sensitivity scales. The PC tracker scales its own output, so this is only
        /// for poses computed elsewhere.
        /// </summary>
        public HeadPose ApplyScales(HeadPose pose)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }

            return new HeadPose
            {
                X = pose.X * TranslationScale,
                Y = pose.Y * TranslationScale,
                Z = pose.Z * TranslationScale,
                Yaw = pose.Yaw * YawScale,
                Pitch = pose.Pitch * PitchScale,
                Roll = pose.Roll * RollScale,
                FaceBox = pose.FaceBox,
                Landmarks = pose.Landmarks
            };
        }

        /// <summary>
        /// 0 = no extra filtering, 0.95 = heaviest. Shares the tracking Smoothing slider.
        /// </summary>
        public double SmoothingStrength
        {
            get
            {
                lock (_lock)
                {
                    return _smoothingStrength;
                }
            }
            set
            {
                lock (_lock)
                {
                    _smoothingStrength = Math.Clamp(value, 0.0, 0.95);
                    ApplySmoothing();
                }
            }
        }

        private void ApplySmoothing()
        {
            // Map slider to a stationary cutoff: 0 -> 3.3 Hz (light), 0.5 -> 0.9 Hz (default), 0.7 -> 0.6 Hz, 0.95 -> 0.4 Hz.
            // Measured on synthetic 1.5 deg landmark noise: ~1 Hz cuts resting jitter by ~65%
            // while a 30 deg turn still lands within 4 frames.
            double minCutoff = 0.25 + 3.0 * Math.Exp(-3.0 * _smoothingStrength);

            // Rotation axes are in degrees, translation in centimetres; speeds differ by roughly 10x
            for (int i = 0; i < _filters.Length; i++)
            {
                _filters[i].MinCutoff = minCutoff;
                _filters[i].Beta = i < 3 ? 0.2 : 0.02;
            }
        }

        private void ResetFilters()
        {
            foreach (var filter in _filters)
            {
                filter.Reset();
            }
        }

        /// <summary>
        /// True to bypass Opentrack and write to the game directly.
        /// </summary>
        public bool DirectOutputEnabled
        {
            get
            {
                lock (_lock)
                {
                    return _directOutputEnabled;
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_directOutputEnabled == value)
                    {
                        return;
                    }

                    _directOutputEnabled = value;

                    ResetFilters();

                    if (value)
                    {
                        _freeTrackSender.Open();
                        _centerPending = true;
                        _centreRequestedAt = -1.0;
                        _centreSamples.Clear();
                        _center = null;
                    }
                    else
                    {
                        _freeTrackSender.Close();
                        _center = null;
                    }

                    Debug.WriteLine($"[PoseOutputRouter] Direct output {(value ? "enabled" : "disabled")}");
                }
            }
        }

        /// <summary>
        /// Human-readable state of the active output for the UI.
        /// </summary>
        public string Status
        {
            get
            {
                lock (_lock)
                {
                    if (_directOutputEnabled)
                    {
                        if (_centerPending || _center == null)
                        {
                            return $"{_freeTrackSender.Status}. Centring: sit normally and look straight ahead for a few seconds.";
                        }

                        return _freeTrackSender.Status;
                    }

                    return _udpSender != null && _udpSender.IsConnected
                        ? "Sending to Opentrack (UDP 127.0.0.1:4242)"
                        : "Opentrack UDP sender not connected";
                }
            }
        }

        /// <summary>
        /// Capture the next pose as the zero reference for direct output.
        /// </summary>
        public void Center()
        {
            lock (_lock)
            {
                _centerPending = true;
                _centreRequestedAt = -1.0;
                _centreSamples.Clear();
                ResetFilters();
            }
        }

        public async Task SendPoseAsync(HeadPose pose)
        {
            if (pose == null)
            {
                return;
            }

            bool direct;
            HeadPose? center;
            double now = _clock.Elapsed.TotalSeconds;

            lock (_lock)
            {
                direct = _directOutputEnabled;

                if (direct && (_centerPending || _center == null))
                {
                    if (_centreRequestedAt < 0)
                    {
                        _centreRequestedAt = now;
                        _centreSamples.Clear();
                    }

                    double elapsed = now - _centreRequestedAt;

                    if (elapsed >= CentreSettleSeconds)
                    {
                        _centreSamples.Add(pose);
                    }

                    if (elapsed >= CentreSettleSeconds + CentreAverageSeconds && _centreSamples.Count > 0)
                    {
                        _center = new HeadPose
                        {
                            X = _centreSamples.Average(p => p.X),
                            Y = _centreSamples.Average(p => p.Y),
                            Z = _centreSamples.Average(p => p.Z),
                            Yaw = _centreSamples.Average(p => p.Yaw),
                            Pitch = _centreSamples.Average(p => p.Pitch),
                            Roll = _centreSamples.Average(p => p.Roll)
                        };
                        Debug.WriteLine($"[PoseOutputRouter] Centre captured from {_centreSamples.Count} samples: {_center}");
                        _centerPending = false;
                        _centreRequestedAt = -1.0;
                        _centreSamples.Clear();
                        ResetFilters();
                    }
                    else
                    {
                        // Hold the game at neutral until the centre is known
                        _freeTrackSender.WritePose(0, 0, 0, 0, 0, 0);
                        return;
                    }
                }

                center = _center;
            }

            // Poses arrive unscaled. Opentrack centres and maps for itself, so it gets the scaled absolute pose.
            if (!direct)
            {
                if (_udpSender != null && _udpSender.IsConnected)
                {
                    await _udpSender.SendPoseAsync(ApplyScales(pose));
                }

                return;
            }

            if (center == null)
            {
                return;
            }

            // Direct mode: centre on the raw pose, filter the movement, then apply gain to the movement only.
            // Scaling before centring would multiply the absolute head position and pin every axis at full scale.
            double t = _clock.Elapsed.TotalSeconds;
            double x, y, z, yaw, pitch, roll;
            double translationScale, yawScale, pitchScale, rollScale;

            lock (_lock)
            {
                x = _filters[0].Filter(pose.X - center.X, t);
                y = _filters[1].Filter(pose.Y - center.Y, t);
                z = _filters[2].Filter(pose.Z - center.Z, t);
                yaw = _filters[3].Filter(pose.Yaw - center.Yaw, t);
                pitch = _filters[4].Filter(pose.Pitch - center.Pitch, t);
                roll = _filters[5].Filter(pose.Roll - center.Roll, t);

                translationScale = TranslationScale;
                yawScale = YawScale;
                pitchScale = PitchScale;
                rollScale = RollScale;
            }

            _freeTrackSender.WritePose(
                yaw * yawScale,
                pitch * pitchScale,
                roll * rollScale,
                x * translationScale,
                y * translationScale,
                z * translationScale);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _freeTrackSender.Dispose();
            }
        }
    }
}
