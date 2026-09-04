using System;
using System.Numerics;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// Iterative Perspective-n-Point solver.
    /// Minimises reprojection error over an axis-angle rotation and a translation
    /// using Levenberg-Marquardt with a finite-difference Jacobian.
    ///
    /// Camera convention: X right, Y down, Z forward (away from the camera).
    /// Pinhole model with no lens distortion.
    /// </summary>
    internal static class PnpSolver
    {
        private const int MaxIterations = 50;
        private const int MaxDampingAttempts = 10;
        private const double RotationStep = 1e-6;
        private const double TranslationStep = 1e-3;
        private const double MinDepth = 1e-6;

        /// <summary>
        /// Refine a 6DOF pose so that <paramref name="modelPoints"/> project onto <paramref name="imagePoints"/>.
        /// </summary>
        /// <param name="modelPoints">3D points in model space (same units as the resulting translation).</param>
        /// <param name="imagePoints">Observed 2D pixel positions, one per model point.</param>
        /// <param name="fx">Horizontal focal length in pixels.</param>
        /// <param name="fy">Vertical focal length in pixels.</param>
        /// <param name="cx">Principal point X in pixels.</param>
        /// <param name="cy">Principal point Y in pixels.</param>
        /// <param name="rvec">Axis-angle rotation. In: initial guess. Out: refined solution.</param>
        /// <param name="tvec">Translation. In: initial guess. Out: refined solution.</param>
        /// <param name="rmsPx">Root-mean-square reprojection error per point, in pixels.</param>
        /// <returns>True if a finite solution in front of the camera was found.</returns>
        public static bool TrySolve(
            Vector3[] modelPoints,
            Vector2[] imagePoints,
            double fx,
            double fy,
            double cx,
            double cy,
            double[] rvec,
            double[] tvec,
            out double rmsPx)
        {
            rmsPx = double.PositiveInfinity;

            if (modelPoints == null || imagePoints == null || rvec == null || tvec == null)
            {
                return false;
            }

            if (modelPoints.Length < 4 || modelPoints.Length != imagePoints.Length)
            {
                return false;
            }

            if (rvec.Length != 3 || tvec.Length != 3)
            {
                return false;
            }

            int n = modelPoints.Length;
            int m = 2 * n;

            var p = new double[6] { rvec[0], rvec[1], rvec[2], tvec[0], tvec[1], tvec[2] };
            var residuals = new double[m];
            var trial = new double[m];
            var perturbed = new double[m];
            var jac = new double[m, 6];
            var jtj = new double[6, 6];
            var jtr = new double[6];
            var a = new double[6, 6];
            var rhs = new double[6];
            var delta = new double[6];
            var pTrial = new double[6];
            var pPerturbed = new double[6];

            if (!ComputeResiduals(p, modelPoints, imagePoints, fx, fy, cx, cy, residuals))
            {
                return false;
            }

            double cost = Dot(residuals, residuals);
            double lambda = 1e-3;

            for (int iter = 0; iter < MaxIterations; iter++)
            {
                // Forward-difference Jacobian
                for (int j = 0; j < 6; j++)
                {
                    Array.Copy(p, pPerturbed, 6);
                    double step = j < 3 ? RotationStep : TranslationStep;
                    pPerturbed[j] += step;

                    if (!ComputeResiduals(pPerturbed, modelPoints, imagePoints, fx, fy, cx, cy, perturbed))
                    {
                        return Finish(p, rvec, tvec, cost, n, out rmsPx);
                    }

                    for (int i = 0; i < m; i++)
                    {
                        jac[i, j] = (perturbed[i] - residuals[i]) / step;
                    }
                }

                // Normal equations
                for (int r = 0; r < 6; r++)
                {
                    jtr[r] = 0.0;
                    for (int c = 0; c < 6; c++)
                    {
                        double sum = 0.0;
                        for (int i = 0; i < m; i++)
                        {
                            sum += jac[i, r] * jac[i, c];
                        }
                        jtj[r, c] = sum;
                    }

                    for (int i = 0; i < m; i++)
                    {
                        jtr[r] += jac[i, r] * residuals[i];
                    }
                }

                bool improved = false;
                double stepNormRot = 0.0;
                double stepNormTrans = 0.0;

                for (int attempt = 0; attempt < MaxDampingAttempts; attempt++)
                {
                    for (int r = 0; r < 6; r++)
                    {
                        for (int c = 0; c < 6; c++)
                        {
                            a[r, c] = jtj[r, c];
                        }
                        a[r, r] += lambda * Math.Max(jtj[r, r], 1e-12);
                        rhs[r] = -jtr[r];
                    }

                    if (!SolveLinear(a, rhs, delta))
                    {
                        lambda *= 10.0;
                        continue;
                    }

                    for (int k = 0; k < 6; k++)
                    {
                        pTrial[k] = p[k] + delta[k];
                    }

                    if (ComputeResiduals(pTrial, modelPoints, imagePoints, fx, fy, cx, cy, trial))
                    {
                        double trialCost = Dot(trial, trial);
                        if (trialCost < cost)
                        {
                            Array.Copy(pTrial, p, 6);
                            var swap = residuals;
                            residuals = trial;
                            trial = swap;
                            cost = trialCost;
                            lambda = Math.Max(lambda / 3.0, 1e-9);
                            improved = true;

                            stepNormRot = Math.Sqrt(delta[0] * delta[0] + delta[1] * delta[1] + delta[2] * delta[2]);
                            stepNormTrans = Math.Sqrt(delta[3] * delta[3] + delta[4] * delta[4] + delta[5] * delta[5]);
                            break;
                        }
                    }

                    lambda *= 5.0;
                }

                if (!improved)
                {
                    break;
                }

                if (stepNormRot < 1e-7 && stepNormTrans < 1e-4)
                {
                    break;
                }
            }

            return Finish(p, rvec, tvec, cost, n, out rmsPx);
        }

        /// <summary>
        /// Convert an axis-angle vector to a row-major 3x3 rotation matrix.
        /// </summary>
        public static void Rodrigues(double rx, double ry, double rz, double[] r)
        {
            if (r == null || r.Length < 9)
            {
                throw new ArgumentException("Rotation output must have 9 elements", nameof(r));
            }

            double theta = Math.Sqrt(rx * rx + ry * ry + rz * rz);

            if (theta < 1e-12)
            {
                r[0] = 1; r[1] = 0; r[2] = 0;
                r[3] = 0; r[4] = 1; r[5] = 0;
                r[6] = 0; r[7] = 0; r[8] = 1;
                return;
            }

            double kx = rx / theta;
            double ky = ry / theta;
            double kz = rz / theta;
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            double v = 1.0 - c;

            r[0] = c + kx * kx * v;
            r[1] = kx * ky * v - kz * s;
            r[2] = kx * kz * v + ky * s;

            r[3] = ky * kx * v + kz * s;
            r[4] = c + ky * ky * v;
            r[5] = ky * kz * v - kx * s;

            r[6] = kz * kx * v - ky * s;
            r[7] = kz * ky * v + kx * s;
            r[8] = c + kz * kz * v;
        }

        private static bool Finish(double[] p, double[] rvec, double[] tvec, double cost, int n, out double rmsPx)
        {
            rmsPx = double.PositiveInfinity;

            for (int k = 0; k < 6; k++)
            {
                if (double.IsNaN(p[k]) || double.IsInfinity(p[k]))
                {
                    return false;
                }
            }

            if (p[5] <= MinDepth)
            {
                return false;
            }

            rvec[0] = p[0];
            rvec[1] = p[1];
            rvec[2] = p[2];
            tvec[0] = p[3];
            tvec[1] = p[4];
            tvec[2] = p[5];

            rmsPx = Math.Sqrt(cost / n);
            return !double.IsNaN(rmsPx);
        }

        private static bool ComputeResiduals(
            double[] p,
            Vector3[] modelPoints,
            Vector2[] imagePoints,
            double fx,
            double fy,
            double cx,
            double cy,
            double[] residuals)
        {
            Span<double> r = stackalloc double[9];
            RodriguesSpan(p[0], p[1], p[2], r);

            double tx = p[3];
            double ty = p[4];
            double tz = p[5];

            for (int i = 0; i < modelPoints.Length; i++)
            {
                var mp = modelPoints[i];
                double x = r[0] * mp.X + r[1] * mp.Y + r[2] * mp.Z + tx;
                double y = r[3] * mp.X + r[4] * mp.Y + r[5] * mp.Z + ty;
                double z = r[6] * mp.X + r[7] * mp.Y + r[8] * mp.Z + tz;

                if (z <= MinDepth)
                {
                    return false;
                }

                double u = fx * x / z + cx;
                double v = fy * y / z + cy;

                residuals[2 * i] = u - imagePoints[i].X;
                residuals[2 * i + 1] = v - imagePoints[i].Y;
            }

            return true;
        }

        private static void RodriguesSpan(double rx, double ry, double rz, Span<double> r)
        {
            double theta = Math.Sqrt(rx * rx + ry * ry + rz * rz);

            if (theta < 1e-12)
            {
                r[0] = 1; r[1] = 0; r[2] = 0;
                r[3] = 0; r[4] = 1; r[5] = 0;
                r[6] = 0; r[7] = 0; r[8] = 1;
                return;
            }

            double kx = rx / theta;
            double ky = ry / theta;
            double kz = rz / theta;
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            double v = 1.0 - c;

            r[0] = c + kx * kx * v;
            r[1] = kx * ky * v - kz * s;
            r[2] = kx * kz * v + ky * s;

            r[3] = ky * kx * v + kz * s;
            r[4] = c + ky * ky * v;
            r[5] = ky * kz * v - kx * s;

            r[6] = kz * kx * v - ky * s;
            r[7] = kz * ky * v + kx * s;
            r[8] = c + kz * kz * v;
        }

        /// <summary>
        /// Solve a 6x6 linear system with Gaussian elimination and partial pivoting.
        /// </summary>
        private static bool SolveLinear(double[,] a, double[] b, double[] x)
        {
            const int size = 6;
            var work = new double[size, size + 1];

            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    work[r, c] = a[r, c];
                }
                work[r, size] = b[r];
            }

            for (int col = 0; col < size; col++)
            {
                int pivot = col;
                double best = Math.Abs(work[col, col]);
                for (int r = col + 1; r < size; r++)
                {
                    double candidate = Math.Abs(work[r, col]);
                    if (candidate > best)
                    {
                        best = candidate;
                        pivot = r;
                    }
                }

                if (best < 1e-14)
                {
                    return false;
                }

                if (pivot != col)
                {
                    for (int c = 0; c <= size; c++)
                    {
                        (work[col, c], work[pivot, c]) = (work[pivot, c], work[col, c]);
                    }
                }

                for (int r = col + 1; r < size; r++)
                {
                    double factor = work[r, col] / work[col, col];
                    if (factor == 0.0)
                    {
                        continue;
                    }

                    for (int c = col; c <= size; c++)
                    {
                        work[r, c] -= factor * work[col, c];
                    }
                }
            }

            for (int r = size - 1; r >= 0; r--)
            {
                double sum = work[r, size];
                for (int c = r + 1; c < size; c++)
                {
                    sum -= work[r, c] * x[c];
                }
                x[r] = sum / work[r, r];
            }

            return true;
        }

        private static double Dot(double[] a, double[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                sum += a[i] * b[i];
            }
            return sum;
        }
    }
}
