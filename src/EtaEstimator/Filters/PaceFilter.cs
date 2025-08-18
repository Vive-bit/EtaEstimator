// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Filters
{
    internal sealed class PaceFilter
    {
        public bool HasValue => _m is not null;
        public double Value => _m!.Value;
        public double Var => _p!.Value;

        private double? _m;
        private double? _p; 
        private double? _r;

        public void Update(double z, double driftFactor, double noiseBlend)
        {
            if (_m is null)
            {
                _m = z;
                _p = 1.0;
                _r = System.Math.Max(1e-9, 0.01 * z * z + 1e-9);
                return;
            }

            double innov = z - _m.Value;
            _r = (1 - noiseBlend) * _r!.Value + noiseBlend * (innov * innov);
            double Q = System.Math.Max(1e-12, driftFactor * _r.Value);
            double R = System.Math.Max(1e-9, _r.Value);

            double mPred = _m.Value;
            double pPred = _p.Value + Q;

            double y = z - mPred;
            double S = pPred + R;
            double K = pPred / S;

            _m = mPred + K * y;
            _p = (1 - K) * pPred;
        }
    }

}
