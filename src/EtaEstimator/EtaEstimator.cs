// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator
{
    using System;
    using System.Diagnostics;
    using Filters;
    using Regression;
    using Stats;
    using Utils;

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
        double NearEndSnapSec = 8.0
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

    public sealed class ETAEstimator
    {
        private readonly object _gate = new();
        private readonly ITimeSource _time;
        private readonly EtaOptions _opt;

        private readonly StepStats _stats = new();
        private readonly NoiseMeter _noise = new();
        private readonly PaceFilter _pace = new();
        private readonly ProgressTrend _trend;

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

        public ETAEstimator(double totalUnits, EtaOptions? options = null, ITimeSource? time = null)
        {
            if (totalUnits <= 0) throw new ArgumentOutOfRangeException(nameof(totalUnits));
            _total = totalUnits;
            _opt = options ?? new EtaOptions();
            _time = time ?? new SystemTimeSource();
            _trend = new ProgressTrend(_opt.Forget);
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

                double alpha = (_obs < _opt.WarmupSamples) ? _opt.EmaAlphaWarmup : _opt.EmaAlpha;
                _emaSpu = double.IsNaN(_emaSpu) ? secPerUnit
                    : alpha * secPerUnit + (1 - alpha) * _emaSpu;

                double baseMean = _stats.Count > 0 ? _stats.Mean : secPerUnit;
                double resid = secPerUnit - baseMean;
                _noise.Push(resid);
                double? sigma = _noise.Sigma();
                double w = Calc.Huber(resid, sigma, _opt.OutlierCut);

                double forMean = (_stats.Count == 0 || w >= 1.0) ? secPerUnit
                    : _stats.Mean + w * (secPerUnit - _stats.Mean);
                _stats.Push(forMean);

                double forPace = !_pace.HasValue ? secPerUnit : _pace.Value + w * (secPerUnit - _pace.Value);
                _pace.Update(forPace, _opt.DriftFactor, _opt.NoiseBlend);

                double newDone = Math.Min(_total, _done + Math.Max(0.0, units));
                double elapsed = now - _t0;
                _trend.Update(1.0, newDone, elapsed);
                _done = newDone;

                _progressedSinceLastRead = true;
                _obs++;

                return Snapshot();
            }
        }

        public EtaSnapshot Snapshot()
        {
            lock (_gate)
            {
                double left = Math.Max(0.0, _total - _done);
                double percent = _total > 0 ? Math.Min(100.0, 100.0 * _done / _total) : 0.0;

                if (left <= 1e-9) return new EtaSnapshot(0, percent, _emaSpu, _pace.HasValue ? _pace.Value : double.NaN);

                double num = 0, den = 0;

                if (_pace.HasValue && _pace.Value > 0)
                {
                    double eta = left * _pace.Value;
                    double var = Math.Max(1e-9, left * left * Math.Max(1e-9, _pace.Var));
                    Acc(eta, var, ref num, ref den);
                }

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

                if (_stats.Count >= 2 && _stats.Mean > 0)
                {
                    double eta = left * _stats.Mean;
                    double var = Math.Max(1e-9, _stats.Variance * left);
                    Acc(eta, var, ref num, ref den);
                }

                if (_emaSpu > 0)
                {
                    double eta = left * _emaSpu;
                    double var = Math.Max(1e-6, left * left * 0.25);
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

        public double Total => _total;
        public double Done => _done;
        public double Percent => Snapshot().Percent;
    }
}
