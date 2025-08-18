// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Tests
{
    using System.Threading.Tasks;
    using Helper;
    using Xunit;

    public class TrendFitTests
    {
        [Fact]
        public async Task Trend_Predicts_TotalTime_Reasonably()
        {
            var est = new EtaEstimator(totalUnits: 40, forget: 0.998);

            // konstante 25ms pro Einheit
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(25);
                est.Step(1);
            }

            // Halbzeit: restliche ETA ~ 20 * 0.025 = 0.5s
            var eta = est.GetEtaSeconds();
            AssertEx.Approximately(eta, expected: 0.5, relTol: 0.45, absTol: 0.35);
        }
    }

}
