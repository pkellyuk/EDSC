using EDSC.Services;
using System;
using System.Diagnostics;
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
                        return _center == null
                            ? $"{_freeTrackSender.Status}. Look straight ahead; first pose sets the centre."
                            : _freeTrackSender.Status;
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

            lock (_lock)
            {
                direct = _directOutputEnabled;

                if (direct && (_centerPending || _center == null))
                {
                    _center = new HeadPose
                    {
                        X = pose.X,
                        Y = pose.Y,
                        Z = pose.Z,
                        Yaw = pose.Yaw,
                        Pitch = pose.Pitch,
                        Roll = pose.Roll
                    };
                    _centerPending = false;
                    Debug.WriteLine($"[PoseOutputRouter] Centre captured: {_center}");
                }

                center = _center;
            }

            if (!direct)
            {
                if (_udpSender != null && _udpSender.IsConnected)
                {
                    await _udpSender.SendPoseAsync(pose);
                }

                return;
            }

            if (center == null)
            {
                return;
            }

            double t = _clock.Elapsed.TotalSeconds;
            double x, y, z, yaw, pitch, roll;

            lock (_lock)
            {
                x = _filters[0].Filter(pose.X - center.X, t);
                y = _filters[1].Filter(pose.Y - center.Y, t);
                z = _filters[2].Filter(pose.Z - center.Z, t);
                yaw = _filters[3].Filter(pose.Yaw - center.Yaw, t);
                pitch = _filters[4].Filter(pose.Pitch - center.Pitch, t);
                roll = _filters[5].Filter(pose.Roll - center.Roll, t);
            }

            _freeTrackSender.WritePose(yaw, pitch, roll, x, y, z);
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
