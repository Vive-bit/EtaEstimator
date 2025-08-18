// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator
{
    using System;
    using System.Diagnostics;
    using Filters;
    using Regression;
    using Stats;
    using Utils;
    using System.Collections.Generic;

    public sealed record EtaOptions(
        double OutlierCut = 3.0,
        double NoiseBlend = 0.15,
        double DriftFactor = 0.02,
        double Forget = 0.995,
        double EmaAlpha = 0.12,
        double MaxDropPerSec = 1.0,
        double RiseGraceSec = 3.0,
        int RiseMinJump = 2,
        double ColdStartSecPerUnit = 0.4,
        int WarmupSamples = 20,
        double EmaAlphaWarmup = 0.37,
        double MaxLagAtEnd = 1.5,
        double LagSlopeSqrt = 0.75,
        double NearEndSnapSec = 8.0,
        // Neu: robustere Pace + Regime-Shift
        double QuantileP = 0.70,      // p60–p80 ist i. d. R. gut
        double RegimeAlpha = 0.10,    // Rolling-Mean/MAD Glättung
        double RegimeThreshold = 4.0, // Shift, wenn |x-mean| > k * MAD
        int RegimeWarmupSteps = 8  // so viele Steps aggressiver lernen
    );

    public interface ITimeSource { double NowSeconds { get; } }
    public sealed class SystemTimeSource : ITimeSource
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        public double NowSeconds => _sw.Elapsed.TotalSeconds;
    }

    public readonly record struct EtaSnapshot(
        double RemainingSeconds,
        double Percent,
        double SecPerUnitEma,
        double SecPerUnitFiltered);

    // --- O(1) P²-Quantilschätzer (Jain/Chlamtac) ---
    internal sealed class P2Quantile
    {
        private readonly double p;
        private readonly double[] q = new double[5];
        private readonly double[] n = new double[5];
        private readonly double[] np = new double[5];
        private readonly double[] dn = new double[5];
        private int count = 0;

        public P2Quantile(double p)
        {
            if (p <= 0 || p >= 1) throw new ArgumentOutOfRangeException(nameof(p));
            this.p = p;
        }

        public void Reset()
        {
            Array.Clear(q, 0, q.Length);
            Array.Clear(n, 0, n.Length);
            Array.Clear(np, 0, np.Length);
            Array.Clear(dn, 0, dn.Length);
            count = 0;
        }

        public void Push(double x)
        {
            if (!double.IsFinite(x)) return;

            if (count < 5)
            {
                q[count++] = x;
                if (count == 5)
                {
                    Array.Sort(q);
                    for (int i = 0; i < 5; i++) n[i] = i + 1;
                    np[0] = 1;
                    np[1] = 1 + 2 * p;
                    np[2] = 1 + 4 * p;
                    np[3] = 3 + 2 * p;
                    np[4] = 5;
                    dn[0] = 0;
                    dn[1] = p / 2;
                    dn[2] = p;
                    dn[3] = (1 + p) / 2;
                    dn[4] = 1;
                }
                return;
            }

            int k;
            if (x < q[0]) { q[0] = x; k = 0; }
            else if (x >= q[4]) { q[4] = x; k = 3; }
            else
            {
                for (k = 0; k < 4; k++) if (x < q[k + 1]) break;
            }

            for (int i = 0; i < 5; i++) n[i] += (i <= k) ? 1 : 0;
            for (int i = 0; i < 5; i++) np[i] += dn[i];

            for (int i = 1; i <= 3; i++)
            {
                double d = np[i] - n[i];
                if ((d >= 1 && n[i + 1] - n[i] > 1) || (d <= -1 && n[i - 1] - n[i] < -1))
                {
                    int sign = Math.Sign(d);
                    double qd = Parabolic(i, sign);
                    if (q[i - 1] < qd && qd < q[i + 1]) q[i] = qd;
                    else q[i] = Linear(i, sign);
                    n[i] += sign;
                }
            }
        }

        public double Value => (count < 5) ? double.NaN : q[2];

        private double Parabolic(int i, int d)
        {
            double n0 = n[i - 1], n1 = n[i], n2 = n[i + 1];
            double q0 = q[i - 1], q1 = q[i], q2 = q[i + 1];
            return q1 + d * ((n1 - n0 + d) * (q2 - q1) / (n2 - n1) +
                             (n2 - n1 - d) * (q1 - q0) / (n1 - n0)) / (n2 - n0);
        }
        private double Linear(int i, int d) => q[i] + d * (q[i + d] - q[i]) / (n[i + d] - n[i]);
    }

    public sealed class ETAEstimator
    {
        private readonly object _gate = new();
        private readonly ITimeSource _time;
        private readonly EtaOptions _opt;

        private readonly StepStats _stats = new();
        private readonly NoiseMeter _noise = new();
        private readonly PaceFilter _pace = new();
        private readonly ProgressTrend _trend;

        private readonly P2Quantile _qSpu; // NEU: robustes Pace-Quantil

        private double _total;
        private double _done;
        private double _t0;
        private double _tLast;

        private double _emaSpu = double.NaN;

        private int? _dispEta;
        private bool _progressedSinceLastRead;
        private double _dropCreditSec = 0.0;
        private int _obs = 0;
        private double _lastWallSec = 0.0;
        private double _lastDecreaseSec = double.NaN;
        private DateTime _lastProgress = DateTime.UtcNow;

        // Regime-Shift Heuristik (Rolling-Mean/MAD, exponentiell)
        private double _winMean = double.NaN, _winMad = double.NaN;
        private int _winN = 0;
        private int _warmupOverride = 0;

        public ETAEstimator(double totalUnits, EtaOptions? options = null, ITimeSource? time = null)
        {
            if (totalUnits <= 0) throw new ArgumentOutOfRangeException(nameof(totalUnits));
            _total = totalUnits;
            _opt = options ?? new EtaOptions();
            _time = time ?? new SystemTimeSource();
            _trend = new ProgressTrend(_opt.Forget);
            _qSpu = new P2Quantile(_opt.QuantileP);
        }

        public void Reset(double totalUnits)
        {
            if (totalUnits <= 0) throw new ArgumentOutOfRangeException(nameof(totalUnits));
            lock (_gate)
            {
                _total = totalUnits;
                _done = 0;
                _t0 = _tLast = 0;

                _emaSpu = (_opt.ColdStartSecPerUnit > 0) ? _opt.ColdStartSecPerUnit : double.NaN;

                _dispEta = null;
                _lastWallSec = 0.0;
                _progressedSinceLastRead = false;
                _dropCreditSec = 0.0;
                _lastDecreaseSec = double.NaN;
                _obs = 0;

                _winMean = double.NaN; _winMad = 0.0; _winN = 0;
                _warmupOverride = 0;
                _qSpu.Reset();
            }
        }

        public EtaSnapshot Step(double units = 1.0)
        {
            if (units <= 0) return Snapshot();

            lock (_gate)
            {
                double now = _time.NowSeconds;
                if (_t0 == 0) _t0 = now;

                if (_tLast == 0)
                {
                    _tLast = now;
                    _done = Math.Min(_total, _done + Math.Max(0.0, units));
                    _progressedSinceLastRead = true;
                    return Snapshot();
                }

                double dt = now - _tLast;
                _tLast = now;
                if (dt <= 0) dt = 1e-9;

                double secPerUnit = dt / units;
                if (!double.IsFinite(secPerUnit) || secPerUnit <= 0) secPerUnit = 1e-9;

                // Regime-Shift erkennen -> kurzfristig aggressiver lernen
                if (IsRegimeShift(secPerUnit))
                    _warmupOverride = Math.Max(_warmupOverride, _opt.RegimeWarmupSteps);
                else if (_warmupOverride > 0)
                    _warmupOverride--;

                // EMA (Warmup oder Override)
                double alpha = (_obs < _opt.WarmupSamples || _warmupOverride > 0) ? _opt.EmaAlphaWarmup : _opt.EmaAlpha;
                _emaSpu = double.IsNaN(_emaSpu) ? secPerUnit
                    : alpha * secPerUnit + (1 - alpha) * _emaSpu;

                // Robustheit + Noise + Welford
                double baseMean = _stats.Count > 0 ? _stats.Mean : secPerUnit;
                double resid = secPerUnit - baseMean;
                _noise.Push(resid);
                double? sigma = _noise.Sigma();
                double w = Calc.Huber(resid, sigma, _opt.OutlierCut);

                double forMean = (_stats.Count == 0 || w >= 1.0) ? secPerUnit
                    : _stats.Mean + w * (secPerUnit - _stats.Mean);
                _stats.Push(forMean);

                // Pace-Filter: bei Override schneller adaptieren
                double drift = (_warmupOverride > 0) ? Math.Min(0.5, _opt.DriftFactor * 3.0) : _opt.DriftFactor;
                double noiseBlend = (_warmupOverride > 0) ? Math.Min(0.90, _opt.NoiseBlend + 0.50) : _opt.NoiseBlend;

                double forPace = !_pace.HasValue ? secPerUnit : _pace.Value + w * (secPerUnit - _pace.Value);
                _pace.Update(forPace, drift, noiseBlend);

                // Fortschritt & RLS
                double newDone = Math.Min(_total, _done + Math.Max(0.0, units));
                double elapsed = now - _t0;
                _trend.Update(1.0, newDone, elapsed);
                _done = newDone;

                _progressedSinceLastRead = true;
                _obs++;

                // Rolling-Stats + Quantil füttern
                PushWindow(secPerUnit);
                _qSpu.Push(secPerUnit);

                return Snapshot();
            }
        }

        public EtaSnapshot Snapshot()
        {
            lock (_gate)
            {
                double left = Math.Max(0.0, _total - _done);
                double percent = _total > 0 ? Math.Min(100.0, 100.0 * _done / _total) : 0.0;

                if (left <= 1e-9)
                    return new EtaSnapshot(0, percent, _emaSpu, _pace.HasValue ? _pace.Value : double.NaN);

                double num = 0, den = 0;

                // 1) Pace
                if (_pace.HasValue && _pace.Value > 0)
                {
                    double eta = left * _pace.Value;
                    double var = Math.Max(1e-9, left * left * Math.Max(1e-9, _pace.Var));
                    Acc(eta, var, ref num, ref den);
                }

                // 2) RLS
                double elapsed = (_t0 == 0) ? 0 : (_time.NowSeconds - _t0);
                double predTotal = _trend.A + _trend.B * _total;
                predTotal = Math.Max(elapsed, predTotal);
                double etaR = Math.Max(0.0, predTotal - elapsed);
                if (_trend.ResVar is double rv)
                {
                    double xtPx = _trend.XtPx(1.0, _total);
                    double var = Math.Max(1e-9, rv * Math.Max(1e-9, xtPx));
                    Acc(etaR, var, ref num, ref den);
                }

                // 3) Welford
                if (_stats.Count >= 2 && _stats.Mean > 0)
                {
                    double eta = left * _stats.Mean;
                    double var = Math.Max(1e-9, _stats.Variance * left);
                    Acc(eta, var, ref num, ref den);
                }

                // 4) EMA (ruhig)
                if (_emaSpu > 0)
                {
                    double eta = left * _emaSpu;
                    double var = Math.Max(1e-6, left * left * 0.25);
                    Acc(eta, var, ref num, ref den);
                }

                // 5) Quantil-Pace (robust gegen Ausreißer)
                double qv = _qSpu.Value;
                if (!double.IsNaN(qv) && qv > 0)
                {
                    double eta = left * qv;
                    // konservativ, aber fester: stabiler Einfluss
                    double var = Math.Max(1e-6, left * left * 0.10);
                    Acc(eta, var, ref num, ref den);
                }

                double remaining = (den <= 0) ? double.PositiveInfinity : Math.Max(0.0, num / den);
                return new EtaSnapshot(remaining, percent, _emaSpu, _pace.HasValue ? _pace.Value : double.NaN);
            }

            static void Acc(double eta, double var, ref double num, ref double den)
            {
                if (!double.IsFinite(eta) || eta <= 0) return;
                double w = 1.0 / Math.Max(1e-9, var);
                w = Math.Min(w, 1e6);
                num += w * eta;
                den += w;
            }
        }

        public double GetEtaSeconds(bool stable = true)
        {
            var snap = Snapshot();
            if (!stable) return snap.RemainingSeconds;

            if (double.IsInfinity(snap.RemainingSeconds))
                return double.PositiveInfinity;

            if (snap.RemainingSeconds <= 0.25 || _done >= _total - 1e-9)
            {
                _dispEta = 0;
                _dropCreditSec = 0;
                _lastDecreaseSec = _time.NowSeconds;
                _lastWallSec = _lastDecreaseSec;
                return 0;
            }

            double now = _time.NowSeconds;
            double dt = (_lastWallSec == 0.0) ? 0.0 : now - _lastWallSec;
            if (dt < 0) dt = 0;
            _lastWallSec = now;

            _dropCreditSec += Math.Max(0.0, _opt.MaxDropPerSec * dt);

            int target = Math.Max(0, (int)Math.Round(snap.RemainingSeconds, MidpointRounding.AwayFromZero));

            if (_dispEta is null)
            {
                _dispEta = target;
                _progressedSinceLastRead = false;
                return _dispEta.Value;
            }

            int current = _dispEta.Value;

            bool inGraceAfterDrop = (!double.IsNaN(_lastDecreaseSec)) &&
                                    (now - _lastDecreaseSec) < _opt.RiseGraceSec;

            if (target > current)
            {
                if (!inGraceAfterDrop && target - current >= _opt.RiseMinJump)
                    _dispEta = target;
                return _dispEta.Value;
            }

            int desiredDrop = current - target;
            if (desiredDrop > 0)
            {
                double lagCap = Math.Max(_opt.MaxLagAtEnd,
                                         _opt.LagSlopeSqrt * Math.Sqrt(Math.Max(0, target)));
                double deficit = desiredDrop - lagCap;
                if (deficit > 0)
                    _dropCreditSec += deficit;

                if (target <= _opt.NearEndSnapSec)
                    _dropCreditSec += desiredDrop;
            }

            int allowedDrop = (int)Math.Floor(_dropCreditSec);
            if (desiredDrop > 0 && allowedDrop > 0)
            {
                int used = Math.Min(desiredDrop, allowedDrop);
                int newVal = current - used;

                if (target == 0 && newVal <= (int)Math.Ceiling(_opt.MaxLagAtEnd))
                {
                    _dispEta = 0;
                    _dropCreditSec = 0;
                    _lastDecreaseSec = now;
                    return 0;
                }

                _dispEta = newVal;
                _dropCreditSec -= used;
                if (used > 0) _lastDecreaseSec = now;
            }

            return _dispEta.Value;
        }

        // --- Regime-Shift Rolling-Stats ---
        private void PushWindow(double x)
        {
            double a = _opt.RegimeAlpha;
            if (double.IsNaN(_winMean))
            {
                _winMean = x; _winMad = 0.0; _winN = 1;
                return;
            }
            double d = Math.Abs(x - _winMean);
            _winMean = (1 - a) * _winMean + a * x;
            _winMad = (1 - a) * _winMad + a * d;
            _winN++;
        }

        private bool IsRegimeShift(double x)
        {
            if (_winN < 8) return false;
            double mad = _winMad + 1e-9;
            return Math.Abs(x - _winMean) > _opt.RegimeThreshold * mad;
        }

        // Public helpers
        public double Total => _total;
        public double Done => _done;
        public double Percent => Snapshot().Percent;
    }
}
