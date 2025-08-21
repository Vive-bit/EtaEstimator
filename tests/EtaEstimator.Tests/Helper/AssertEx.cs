// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Tests.Helper
{
    internal static class AssertEx
    {
        public static void Approximately(double actual, double expected, double relTol = 0.25, double absTol = 0.25)
        {
            // is, if |a-e| <= max(absTol, relTol*|e|)
            var diff = Math.Abs(actual - expected);
            var tol = Math.Max(absTol, Math.Abs(expected) * relTol);
            if (diff > tol)
                throw new Xunit.Sdk.XunitException($"Expected ≈ {expected} (±{tol}), got {actual}");
        }
    }
}
