// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator
{
    using System;
    using System.Diagnostics;
    using Filters;
    using Regression;
    using Stats;
    using Utils;

    public sealed class EtaEstimator
    {
        private readonly double outlierCut;
        private readonly double noiseBlend;
        private readonly double driftFactor;
        private readonly double forget;
        private readonly double? maxDropPerTick;

        // Fortschritt/Zeit
        private double total, done;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double startAt, lastAt;

        // Module
        private readonly StepStats stats = new();
        private readonly NoiseMeter noise = new();
        private readonly PaceFilter pace = new();
        private readonly ProgressTrend trend;

        private double lastEta = double.PositiveInfinity;

        // UI-Helfer
        public double Total => total;
        public double Done => done;
        public double Percent => Math.Min(100.0, 100.0 * done / total);

        public EtaEstimator(double totalUnits,
                            double outlierCut = 3.0,
                            double noiseBlend = 0.15,
                            double driftFactor = 0.02,
                            double forget = 0.995,
                            double? maxDropPerTick = null)
        {
            if (totalUnits <= 0) throw new ArgumentOutOfRangeException(nameof(totalUnits));
            total = totalUnits;
            this.outlierCut = outlierCut;
            this.noiseBlend = noiseBlend;
            this.driftFactor = driftFactor;
            this.forget = forget;
            this.maxDropPerTick = maxDropPerTick;

            trend = new ProgressTrend(forget);
            startAt = lastAt = 0.0;
        }

        public double Step(double units = 1.0)
        {
            if (units <= 0) return GetEtaSeconds();

            double now = clock.Elapsed.TotalSeconds;
            if (startAt == 0.0) startAt = now;
            double dt = now - (lastAt == 0.0 ? now : lastAt);
            lastAt = now;

            double secPerUnit = dt / units;

            // Robustheit
            double baseMean = stats.Count > 0 ? stats.Mean : secPerUnit;
            double resid = secPerUnit - baseMean;
            noise.Push(resid);
            double? sigma = noise.Sigma();
            double w = Calc.Huber(resid, sigma, outlierCut);

            // Stats
            double forMean = (stats.Count == 0 || w >= 1.0)
                ? secPerUnit
                : stats.Mean + w * (secPerUnit - stats.Mean);
            stats.Push(forMean);

            // Filter
            double forPace = !pace.HasValue
                ? secPerUnit
                : pace.Value + w * (secPerUnit - pace.Value);
            pace.Update(forPace, driftFactor, noiseBlend);

            // Trend
            double doneNew = done + units;
            double elapsed = now - startAt;
            trend.Update(1.0, doneNew, elapsed);

            done = doneNew;

            var eta = GetEtaSeconds();

            if (maxDropPerTick is not null)
            {
                if (!double.IsInfinity(lastEta))
                    eta = Math.Max(0.0, Math.Min(lastEta, Math.Max(eta, lastEta - maxDropPerTick.Value)));
                lastEta = eta;
            }
            return eta;
        }

        public double GetEtaSeconds()
        {
            double left = Math.Max(0.0, total - done);
            if (left <= 0.0) return 0.0;

            double num = 0.0, den = 0.0;

            // PaceFilter-Kandidat
            if (pace.HasValue && pace.Value > 0.0)
            {
                double eta = left * pace.Value;
                double var = Math.Max(1e-12, left * left * Math.Max(1e-12, pace.Var));
                Calc.Collect(eta, var, ref num, ref den);
            }

            // ProgressTrend-Kandidat (Gesamtzeit minus schon verstrichen)
            double predTotal = trend.A + trend.B * total;
            double now = clock.Elapsed.TotalSeconds;
            double etaT = Math.Max(0.0, predTotal - (now - startAt));
            if (trend.ResVar is double rv)
            {
                double xtPx = trend.XtPx(1.0, total);
                double var = Math.Max(1e-12, rv * Math.Max(1e-12, xtPx));
                Calc.Collect(etaT, var, ref num, ref den);
            }

            // Durchschnitts-Kandidat
            if (stats.Count >= 2 && stats.Mean > 0.0)
            {
                double eta = left * stats.Mean;
                double var = Math.Max(1e-12, stats.Variance * left);
                Calc.Collect(eta, var, ref num, ref den);
            }

            if (den <= 0.0) return double.PositiveInfinity;
            return Math.Max(0.0, num / den);
        }

        
    }
}