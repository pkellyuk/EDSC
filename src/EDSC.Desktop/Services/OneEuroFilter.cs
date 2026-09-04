using System;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// One Euro filter (Casiez, Roussel, Vogel 2012). A low-pass filter whose cutoff rises with
    /// speed, so it removes jitter while the signal is still but adds little lag when it moves.
    /// </summary>
    public sealed class OneEuroFilter
    {
        private const double MaxGapSeconds = 0.5;

        private double _minCutoff;
        private double _beta;
        private double _derivativeCutoff;

        private bool _hasPrevious;
        private double _xPrev;
        private double _dxPrev;
        private double _tPrev;

        /// <param name="minCutoff">Cutoff frequency in Hz when stationary. Lower removes more jitter but lags more.</param>
        /// <param name="beta">How quickly the cutoff opens up with speed. Higher tracks fast motion more closely.</param>
        /// <param name="derivativeCutoff">Cutoff for the speed estimate, in Hz.</param>
        public OneEuroFilter(double minCutoff = 1.0, double beta = 0.02, double derivativeCutoff = 1.0)
        {
            _minCutoff = Math.Max(1e-3, minCutoff);
            _beta = Math.Max(0.0, beta);
            _derivativeCutoff = Math.Max(1e-3, derivativeCutoff);
        }

        public double MinCutoff
        {
            get { return _minCutoff; }
            set { _minCutoff = Math.Max(1e-3, value); }
        }

        public double Beta
        {
            get { return _beta; }
            set { _beta = Math.Max(0.0, value); }
        }

        public void Reset()
        {
            _hasPrevious = false;
            _xPrev = 0.0;
            _dxPrev = 0.0;
            _tPrev = 0.0;
        }

        /// <summary>
        /// Filter one sample.
        /// </summary>
        /// <param name="x">Raw value.</param>
        /// <param name="timestampSeconds">Sample time in seconds on any monotonic clock.</param>
        public double Filter(double x, double timestampSeconds)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                return _hasPrevious ? _xPrev : 0.0;
            }

            if (!_hasPrevious)
            {
                _hasPrevious = true;
                _xPrev = x;
                _dxPrev = 0.0;
                _tPrev = timestampSeconds;
                return x;
            }

            double dt = timestampSeconds - _tPrev;
            if (dt <= 0.0)
            {
                dt = 1.0 / 30.0;
            }

            if (dt > MaxGapSeconds)
            {
                // Stream restarted; do not smooth across the gap
                _xPrev = x;
                _dxPrev = 0.0;
                _tPrev = timestampSeconds;
                return x;
            }

            double dx = (x - _xPrev) / dt;
            double alphaD = SmoothingFactor(_derivativeCutoff, dt);
            double dxHat = alphaD * dx + (1.0 - alphaD) * _dxPrev;

            double cutoff = _minCutoff + _beta * Math.Abs(dxHat);
            double alpha = SmoothingFactor(cutoff, dt);
            double xHat = alpha * x + (1.0 - alpha) * _xPrev;

            _xPrev = xHat;
            _dxPrev = dxHat;
            _tPrev = timestampSeconds;
            return xHat;
        }

        private static double SmoothingFactor(double cutoff, double dt)
        {
            double tau = 1.0 / (2.0 * Math.PI * cutoff);
            return 1.0 / (1.0 + tau / dt);
        }
    }
}
