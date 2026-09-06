using EDSC.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// Sends each head pose either to Opentrack over UDP (default) or straight into the
    /// FreeTrack shared memory that the game's NPClient DLL reads (direct mode).
    ///
    /// Opentrack does its own centring and axis mapping. In direct mode this router
    /// subtracts a captured centre pose so the game sees zero when you look straight ahead,
    /// and resamples the incoming pose stream to a steady 120 Hz. A phone delivers poses at
    /// 15-30 per second; written straight to the game that shows as a visible stair-step
    /// every frame. The output thread interpolates between the last samples (and briefly
    /// extrapolates when one is late) so the game sees continuous motion, then filters it.
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

        // Resampling. Raw samples are kept on a timeline built from the phone's own capture
        // timestamps when it sends them, so WiFi jitter does not turn into velocity noise.
        private const int OutputRateHz = 120;
        private const double MinInterval = 0.005;
        private const double MaxInterval = 0.2;
        private const double StreamTimeoutSeconds = 1.0;
        private const double ClockResyncSeconds = 1.0;
        private const int RingSize = 8;

        private struct Sample
        {
            public double T;
            public double X, Y, Z, Yaw, Pitch, Roll;
            public double GazeYaw, GazePitch;
        }

        /// <summary>
        /// What the router last produced, for the preview panel: head direction after centring,
        /// where the eyes look relative to it, and the nudge that was added. Degrees.
        /// </summary>
        public struct OutputSnapshot
        {
            public bool Valid;
            public double Yaw, Pitch;
            public bool HasGaze;
            public double GazeYaw, GazePitch;
            public double NudgeYaw, NudgePitch;
        }

        // Eye gaze. The phone omits gaze while the eyes are shut (a blink), so the last good value
        // is held rather than dropping to zero and back; that would read as a downward flick.
        private const double GazeDeadZoneDegrees = 2.0;
        private double _gazeNudge = 0.2;

        // The nudge is meant to lead the view gently, never to react like the head does, so the
        // gaze gets its own plain low-pass (beta 0 = no speed adaptation) before it is added. A
        // real glance settles in about a quarter of a second; the residue of a blink, a frame or
        // two of pulled iris either side of the hold, is mostly averaged away.
        private const double GazeCutoffHz = 1.5;

        // Blink hold. The phone flags frames where an eye is closing, shut or just reopened; the
        // head matrix is fitted to the eye landmarks too, so pitch dips a little on every blink.
        // Pitch is held at the last clean value for those frames and blended back afterwards.
        private const double BlinkBlendSeconds = 0.15;
        private bool _blinkActive;
        private bool _haveCleanPitch;
        private double _lastCleanPitch;
        private double _blinkHeldPitch;
        private double _blinkEndedAt = -1.0;
        private readonly OneEuroFilter _gazeYawFilter = new OneEuroFilter(GazeCutoffHz, 0.0);
        private readonly OneEuroFilter _gazePitchFilter = new OneEuroFilter(GazeCutoffHz, 0.0);
        private bool _haveGaze;
        private double _heldGazeYaw;
        private double _heldGazePitch;
        private OutputSnapshot _lastOutput;

        private readonly Sample[] _ring = new Sample[RingSize];
        private int _ringCount;
        private int _ringHead;            // index of the newest sample
        private double _meanInterval = 1.0 / 30.0;
        private double _lastSampleTime = double.NaN;
        private double _clockOffset = double.NaN;   // PC seconds minus source seconds

        private Thread? _outputThread;
        private volatile bool _outputRunning;

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
        /// Fraction of the eye gaze angle (past a small dead zone) added to head yaw and pitch. 0 disables.
        /// </summary>
        public double GazeNudge
        {
            get
            {
                lock (_lock)
                {
                    return _gazeNudge;
                }
            }
            set
            {
                lock (_lock)
                {
                    _gazeNudge = double.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);
                }
            }
        }

        /// <summary>
        /// The most recent output directions, for the preview panel.
        /// </summary>
        public OutputSnapshot LastOutput
        {
            get
            {
                lock (_lock)
                {
                    return _lastOutput;
                }
            }
        }

        /// <summary>
        /// Gaze angle to nudge: nothing inside the dead zone, then the excess scaled by the nudge gain.
        /// Called under the lock.
        /// </summary>
        private double Nudge(double gazeDegrees)
        {
            if (!_haveGaze || _gazeNudge <= 0 || double.IsNaN(gazeDegrees))
            {
                return 0.0;
            }

            var magnitude = Math.Abs(gazeDegrees);
            if (magnitude <= GazeDeadZoneDegrees)
            {
                return 0.0;
            }

            return Math.Sign(gazeDegrees) * (magnitude - GazeDeadZoneDegrees) * _gazeNudge;
        }

        /// <summary>
        /// Hold head pitch through a blink and blend it back afterwards. Returns the pose to use.
        /// Called under the lock.
        /// </summary>
        private HeadPose ApplyBlinkHold(HeadPose pose, double now)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }

            if (pose.Blinking)
            {
                if (!_haveCleanPitch)
                {
                    return pose;
                }

                if (!_blinkActive)
                {
                    _blinkActive = true;
                    _blinkHeldPitch = _lastCleanPitch;
                    _blinkEndedAt = -1.0;
                    Debug.WriteLine($"[PoseOutputRouter] Blink: holding pitch at {_blinkHeldPitch:F2}");
                }

                return WithPitch(pose, _blinkHeldPitch);
            }

            if (_blinkActive)
            {
                _blinkActive = false;
                _blinkEndedAt = now;
            }

            _lastCleanPitch = pose.Pitch;
            _haveCleanPitch = true;

            if (_blinkEndedAt < 0)
            {
                return pose;
            }

            var fraction = (now - _blinkEndedAt) / BlinkBlendSeconds;
            if (fraction >= 1.0)
            {
                _blinkEndedAt = -1.0;
                return pose;
            }

            return WithPitch(pose, _blinkHeldPitch + (pose.Pitch - _blinkHeldPitch) * fraction);
        }

        private static HeadPose WithPitch(HeadPose pose, double pitch)
        {
            return new HeadPose
            {
                X = pose.X,
                Y = pose.Y,
                Z = pose.Z,
                Yaw = pose.Yaw,
                Pitch = pitch,
                Roll = pose.Roll,
                FaceBox = pose.FaceBox,
                Landmarks = pose.Landmarks,
                HasGaze = pose.HasGaze,
                GazeYaw = pose.GazeYaw,
                GazePitch = pose.GazePitch,
                Blinking = pose.Blinking
            };
        }

        /// <summary>
        /// Remember the latest measured gaze so a blink holds the last value. Called under the lock.
        /// </summary>
        private void HoldGaze(HeadPose pose)
        {
            if (pose == null || !pose.HasGaze)
            {
                return;
            }

            _heldGazeYaw = pose.GazeYaw;
            _heldGazePitch = pose.GazePitch;
            _haveGaze = true;
        }

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
                Landmarks = pose.Landmarks,
                HasGaze = pose.HasGaze,
                GazeYaw = pose.GazeYaw,
                GazePitch = pose.GazePitch
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

            _gazeYawFilter.Reset();
            _gazePitchFilter.Reset();
        }

        private void ResetResampler()
        {
            _ringCount = 0;
            _ringHead = 0;
            _lastSampleTime = double.NaN;
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
                    ResetResampler();

                    if (value)
                    {
                        _freeTrackSender.Open();
                        _centerPending = true;
                        _centreRequestedAt = -1.0;
                        _centreSamples.Clear();
                        _center = null;
                        StartOutputThread();
                    }
                    else
                    {
                        StopOutputThread();
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

                        return $"{_freeTrackSender.Status}. Output {OutputRateHz} Hz, input ~{InputRateHz:F0}/s.";
                    }

                    return _udpSender != null && _udpSender.IsConnected
                        ? "Sending to Opentrack (UDP 127.0.0.1:4242)"
                        : "Opentrack UDP sender not connected";
                }
            }
        }

        /// <summary>
        /// Poses per second arriving at the router, from the measured sample spacing.
        /// </summary>
        public double InputRateHz
        {
            get
            {
                lock (_lock)
                {
                    return _ringCount > 1 ? 1.0 / _meanInterval : 0.0;
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

        /// <summary>
        /// The pose source stopped (face lost or stream closed). The output holds its last value
        /// rather than extrapolating into nothing.
        /// </summary>
        public void NotifyLost()
        {
            lock (_lock)
            {
                ResetResampler();
            }
        }

        public Task SendPoseAsync(HeadPose pose)
        {
            return SendPoseAsync(pose, null);
        }

        /// <summary>
        /// Accept one raw pose.
        /// </summary>
        /// <param name="pose">Unscaled pose from the tracker.</param>
        /// <param name="sourceTimestampMs">Capture time on the sender's own clock, if known, in milliseconds.</param>
        public async Task SendPoseAsync(HeadPose pose, double? sourceTimestampMs)
        {
            if (pose == null)
            {
                return;
            }

            bool direct;
            double now = _clock.Elapsed.TotalSeconds;

            HeadPose opentrackPose;

            lock (_lock)
            {
                direct = _directOutputEnabled;
                HoldGaze(pose);
                pose = ApplyBlinkHold(pose, now);

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
                        _centreSamples.Add(new HeadPose
                        {
                            X = pose.X,
                            Y = pose.Y,
                            Z = pose.Z,
                            Yaw = pose.Yaw,
                            Pitch = pose.Pitch,
                            Roll = pose.Roll,
                            HasGaze = _haveGaze,
                            GazeYaw = _heldGazeYaw,
                            GazePitch = _heldGazePitch
                        });
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
                            Roll = _centreSamples.Average(p => p.Roll),
                            HasGaze = _haveGaze,
                            GazeYaw = _centreSamples.Average(p => p.GazeYaw),
                            GazePitch = _centreSamples.Average(p => p.GazePitch)
                        };
                        Debug.WriteLine($"[PoseOutputRouter] Centre captured from {_centreSamples.Count} samples: {_center}");
                        _centerPending = false;
                        _centreRequestedAt = -1.0;
                        _centreSamples.Clear();
                        ResetFilters();
                        ResetResampler();
                    }
                    else
                    {
                        // Hold the game at neutral until the centre is known
                        _freeTrackSender.WritePose(0, 0, 0, 0, 0, 0);
                        return;
                    }
                }

                if (direct)
                {
                    PushSample(pose, now, sourceTimestampMs);
                    return;
                }

                // Opentrack does its own centring, so the gaze is used uncentred: it is already
                // relative to the head and rests near zero when looking straight ahead
                var smoothGazeYaw = _gazeYawFilter.Filter(_heldGazeYaw, now);
                var smoothGazePitch = _gazePitchFilter.Filter(_heldGazePitch, now);
                var nudgeYaw = Nudge(smoothGazeYaw);
                var nudgePitch = Nudge(smoothGazePitch);
                opentrackPose = new HeadPose
                {
                    X = pose.X,
                    Y = pose.Y,
                    Z = pose.Z,
                    Yaw = pose.Yaw + nudgeYaw,
                    Pitch = pose.Pitch + nudgePitch,
                    Roll = pose.Roll,
                    FaceBox = pose.FaceBox,
                    Landmarks = pose.Landmarks,
                    HasGaze = _haveGaze,
                    GazeYaw = _heldGazeYaw,
                    GazePitch = _heldGazePitch
                };
                _lastOutput = new OutputSnapshot
                {
                    Valid = true,
                    Yaw = pose.Yaw,
                    Pitch = pose.Pitch,
                    HasGaze = _haveGaze,
                    GazeYaw = _heldGazeYaw,
                    GazePitch = _heldGazePitch,
                    NudgeYaw = nudgeYaw,
                    NudgePitch = nudgePitch
                };
            }

            // Opentrack centres, filters and maps for itself at its own tick rate, so it gets the
            // scaled absolute pose as soon as it arrives.
            if (_udpSender != null && _udpSender.IsConnected)
            {
                await _udpSender.SendPoseAsync(ApplyScales(opentrackPose));
            }
        }

        /// <summary>
        /// Add a raw sample to the timeline. Called under the lock.
        /// </summary>
        private void PushSample(HeadPose pose, double now, double? sourceTimestampMs)
        {
            double t = now;

            if (sourceTimestampMs.HasValue && !double.IsNaN(sourceTimestampMs.Value))
            {
                double source = sourceTimestampMs.Value / 1000.0;
                double delta = now - source;

                if (double.IsNaN(_clockOffset) || Math.Abs(delta - _clockOffset) > ClockResyncSeconds)
                {
                    // First sample, or the sender's clock restarted (page reload)
                    _clockOffset = delta;
                }
                else if (delta < _clockOffset)
                {
                    // A packet that arrived faster than any before: closer to the true offset
                    _clockOffset = delta;
                }
                else
                {
                    // Follow slow clock drift without chasing network delay
                    _clockOffset += (delta - _clockOffset) * 0.002;
                }

                t = source + _clockOffset;
            }

            if (!double.IsNaN(_lastSampleTime))
            {
                double interval = t - _lastSampleTime;
                if (interval <= 0)
                {
                    t = _lastSampleTime + 0.001;
                    interval = 0.001;
                }

                if (now - _lastSampleTime > StreamTimeoutSeconds)
                {
                    // Stream restarted: start the timeline afresh
                    ResetResampler();
                }
                else
                {
                    _meanInterval += (Math.Clamp(interval, MinInterval, MaxInterval) - _meanInterval) * 0.1;
                }
            }

            _lastSampleTime = t;

            _ringHead = (_ringHead + 1) % RingSize;
            _ring[_ringHead] = new Sample
            {
                T = t,
                X = pose.X,
                Y = pose.Y,
                Z = pose.Z,
                Yaw = pose.Yaw,
                Pitch = pose.Pitch,
                Roll = pose.Roll,
                GazeYaw = _heldGazeYaw,
                GazePitch = _heldGazePitch
            };
            if (_ringCount < RingSize)
            {
                _ringCount++;
            }
        }

        private Sample RingAt(int ageFromNewest)
        {
            return _ring[((_ringHead - ageFromNewest) % RingSize + RingSize) % RingSize];
        }

        private static Sample Lerp(in Sample a, in Sample b, double f)
        {
            return new Sample
            {
                X = a.X + (b.X - a.X) * f,
                Y = a.Y + (b.Y - a.Y) * f,
                Z = a.Z + (b.Z - a.Z) * f,
                Yaw = a.Yaw + (b.Yaw - a.Yaw) * f,
                Pitch = a.Pitch + (b.Pitch - a.Pitch) * f,
                Roll = a.Roll + (b.Roll - a.Roll) * f,
                GazeYaw = a.GazeYaw + (b.GazeYaw - a.GazeYaw) * f,
                GazePitch = a.GazePitch + (b.GazePitch - a.GazePitch) * f
            };
        }

        /// <summary>
        /// Pose on the raw timeline at <paramref name="target"/>: interpolated between the two
        /// surrounding samples, extrapolated for at most one sample interval past the newest,
        /// otherwise held. Called under the lock with at least one sample present.
        /// </summary>
        private Sample Resample(double target)
        {
            var newest = RingAt(0);

            if (_ringCount == 1)
            {
                return newest;
            }

            if (target >= newest.T)
            {
                var previous = RingAt(1);
                double span = newest.T - previous.T;
                if (span <= 0)
                {
                    return newest;
                }

                double ahead = Math.Min(target - newest.T, _meanInterval);
                return Lerp(previous, newest, 1.0 + ahead / span);
            }

            for (int age = 1; age < _ringCount; age++)
            {
                var older = RingAt(age);
                var younger = RingAt(age - 1);
                if (target >= older.T)
                {
                    double span = younger.T - older.T;
                    return span <= 0 ? younger : Lerp(older, younger, (target - older.T) / span);
                }
            }

            return RingAt(_ringCount - 1);
        }

        private void StartOutputThread()
        {
            if (_outputThread != null)
            {
                return;
            }

            _outputRunning = true;
            _outputThread = new Thread(OutputLoop)
            {
                IsBackground = true,
                Name = "EDSC pose output",
                Priority = ThreadPriority.AboveNormal
            };
            _outputThread.Start();
        }

        private void StopOutputThread()
        {
            // The loop checks the flag every millisecond and exits on its own; no join, because
            // this is called under the lock the loop also takes.
            _outputRunning = false;
            _outputThread = null;
        }

        private void OutputLoop()
        {
            bool periodRaised = false;
            try
            {
                periodRaised = TimeBeginPeriod(1) == 0;
            }
            catch
            {
                // Not fatal; the sleep just gets coarser
            }

            double period = 1.0 / OutputRateHz;
            double next = _clock.Elapsed.TotalSeconds + period;

            try
            {
                while (_outputRunning)
                {
                    double remaining = next - _clock.Elapsed.TotalSeconds;
                    if (remaining > 0.002)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    while (_clock.Elapsed.TotalSeconds < next)
                    {
                        Thread.SpinWait(50);
                    }

                    next += period;
                    if (_clock.Elapsed.TotalSeconds - next > period * 4)
                    {
                        // Fell behind (debugger, sleep); do not burst to catch up
                        next = _clock.Elapsed.TotalSeconds + period;
                    }

                    OutputTick();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PoseOutputRouter] Output loop stopped: {ex.Message}");
            }
            finally
            {
                if (periodRaised)
                {
                    try
                    {
                        TimeEndPeriod(1);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }

        private void OutputTick()
        {
            double yaw, pitch, roll, x, y, z;
            double translationScale, yawScale, pitchScale, rollScale;

            lock (_lock)
            {
                if (!_directOutputEnabled || _center == null || _centerPending || _ringCount == 0)
                {
                    return;
                }

                double now = _clock.Elapsed.TotalSeconds;
                var newest = RingAt(0);

                if (now - newest.T > StreamTimeoutSeconds + MaxInterval)
                {
                    // Source stopped; leave the game holding the last written pose
                    return;
                }

                // One sample interval of delay puts the target between the two newest samples in the
                // common case, so the output is a true interpolation; extrapolation only covers late packets.
                double delay = Math.Clamp(_meanInterval + 0.004, 0.02, 0.15);
                var sample = Resample(now - delay);

                var center = _center;

                // The gaze nudge joins the head angle before the filter, so its jitter is smoothed
                // by the same adaptive filter as the head and never adds a second noise source
                var headYaw = sample.Yaw - center.Yaw;
                var headPitch = sample.Pitch - center.Pitch;
                var gazeYaw = _gazeYawFilter.Filter(sample.GazeYaw - center.GazeYaw, now);
                var gazePitch = _gazePitchFilter.Filter(sample.GazePitch - center.GazePitch, now);
                var nudgeYaw = Nudge(gazeYaw);
                var nudgePitch = Nudge(gazePitch);

                x = _filters[0].Filter(sample.X - center.X, now);
                y = _filters[1].Filter(sample.Y - center.Y, now);
                z = _filters[2].Filter(sample.Z - center.Z, now);
                yaw = _filters[3].Filter(headYaw + nudgeYaw, now);
                pitch = _filters[4].Filter(headPitch + nudgePitch, now);
                roll = _filters[5].Filter(sample.Roll - center.Roll, now);

                _lastOutput = new OutputSnapshot
                {
                    Valid = true,
                    Yaw = headYaw,
                    Pitch = headPitch,
                    HasGaze = _haveGaze,
                    GazeYaw = gazeYaw,
                    GazePitch = gazePitch,
                    NudgeYaw = nudgeYaw,
                    NudgePitch = nudgePitch
                };

                translationScale = TranslationScale;
                yawScale = YawScale;
                pitchScale = PitchScale;
                rollScale = RollScale;
            }

            // Direct mode: centre on the raw pose, filter the movement, then apply gain to the movement only.
            // Scaling before centring would multiply the absolute head position and pin every axis at full scale.
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
                StopOutputThread();
                _freeTrackSender.Dispose();
            }
        }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint milliseconds);
    }
}
