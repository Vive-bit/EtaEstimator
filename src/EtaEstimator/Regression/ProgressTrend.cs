// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Regression
{
    // RLS: elapsed ≈ A + B·done
    internal sealed class ProgressTrend
    {
        public double A { get; private set; } = 0.0;
        public double B { get; private set; } = 1.0;

        private double P00 = 1e6, P01 = 0.0, P10 = 0.0, P11 = 1e6; // Kovarianz
        public double? ResVar { get; private set; } // EWMA der Residuen
        private readonly double lambda;

        public ProgressTrend(double lambda) => this.lambda = lambda;

        public void Update(double x0, double x1, double y)
        {
            double Px0 = P00 * x0 + P01 * x1;
            double Px1 = P10 * x0 + P11 * x1;
            double denom = lambda + (x0 * Px0 + x1 * Px1);
            double g0 = Px0 / denom;
            double g1 = Px1 / denom;

            double yhat = A * x0 + B * x1;
            double e = y - yhat;

            A += g0 * e;
            B += g1 * e;

            double gP00 = g0 * (x0 * P00 + x1 * P10);
            double gP01 = g0 * (x0 * P01 + x1 * P11);
            double gP10 = g1 * (x0 * P00 + x1 * P10);
            double gP11 = g1 * (x0 * P01 + x1 * P11);

            P00 = (P00 - gP00) / lambda;
            P01 = (P01 - gP01) / lambda;
            P10 = (P10 - gP10) / lambda;
            P11 = (P11 - gP11) / lambda;

            // Symmetrie leicht nachziehen
            double p01 = 0.5 * (P01 + P10);
            P01 = P10 = p01;

            double v = e * e;
            ResVar = ResVar is null ? v : 0.1 * v + 0.9 * ResVar.Value;
        }

        public double XtPx(double x0, double x1)
        {
            double Px0 = P00 * x0 + P01 * x1;
            double Px1 = P10 * x0 + P11 * x1;
            return x0 * Px0 + x1 * Px1;
        }
    }
}
