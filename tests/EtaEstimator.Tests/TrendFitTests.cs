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

            // 20 @ 25ms
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(25);
                est.Step(1);
            }

            // 50/50: remaining ETA ~ 20 * 0.025 = 0.5s
            var eta = est.GetEtaSeconds();
            AssertEx.Approximately(eta, expected: 0.5, relTol: 0.45, absTol: 0.35);
        }
    }

}
