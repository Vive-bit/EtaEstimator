// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Stats
{
    // Welford online: Mittel/Varianz von sek/Einheit
    internal sealed class StepStats
    {
        public int Count { get; private set; }
        public double Mean { get; private set; }
        public double M2 { get; private set; }
        public double Variance => Count >= 2 ? M2 / (Count - 1) : double.NaN;

        public void Push(double x)
        {
            Count++;
            double d = x - Mean;
            Mean += d / Count;
            M2 += d * (x - Mean);
        }
    }
}
