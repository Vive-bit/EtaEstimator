// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Stats
{
    // robuste Sigma-Schätzung via EMAs von |r| und r^2
    internal sealed class NoiseMeter
    {
        private double? absAvg, sqAvg;
        private const double A = 0.2;
        private const double SQRT_PI_OVER_2 = 1.2533141373155001;

        public void Push(double r)
        {
            double ar = System.Math.Abs(r);
            absAvg = absAvg is null ? ar : A * ar + (1 - A) * absAvg.Value;
            double r2 = r * r;
            sqAvg = sqAvg is null ? r2 : A * r2 + (1 - A) * sqAvg.Value;
        }

        public double? Sigma()
        {
            double? s1 = absAvg.HasValue ? absAvg.Value * SQRT_PI_OVER_2 : (double?)null;
            double? s2 = sqAvg.HasValue ? System.Math.Sqrt(sqAvg.Value) : (double?)null;
            if (s1.HasValue && s2.HasValue) return 0.5 * (s1.Value + s2.Value);
            return s1 ?? s2;
        }
    }
}
