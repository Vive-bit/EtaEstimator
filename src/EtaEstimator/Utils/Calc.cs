// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Utils
{
    internal static class Calc
    {
        public static void Collect(double eta, double var, ref double num, ref double den)
        {
            if (eta <= 0.0 || var <= 0.0) return;
            double w = 1.0 / System.Math.Max(1e-12, var);
            num += w * eta;
            den += w;
        }

        public static double Huber(double r, double? sigma, double cut)
        {
            if (sigma is null || sigma.Value <= 1e-12) return 1.0;
            double t = System.Math.Abs(r) / (cut * sigma.Value);
            return (t <= 1.0) ? 1.0 : 1.0 / t;
        }
    }
}
