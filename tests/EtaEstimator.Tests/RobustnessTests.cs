// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Tests
{
    using System.Threading.Tasks;
    using Helper;
    using Xunit;
    using FluentAssertions;

    public class RobustnessTests
    {
        [Fact]
        public async Task Single_Outlier_Does_Not_Destroy_ETA()
        {
            var est = new EtaEstimator(totalUnits: 30, outlierCut: 3.0);

            // 20 Items @ 20ms
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(20);
                est.Step(1);
            }

            var etaBefore = est.GetEtaSeconds();

            // wait
            await Task.Delay(400);
            est.Step(1);

            // then normal again
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(20);
                est.Step(1);
            }

            var etaAfter = est.GetEtaSeconds();

            // Except: no massive jump; ETA can rise, but not too high.
            etaAfter.Should().BeGreaterThan(0);
            // not more than *3 in comparison to before
            (etaAfter <= etaBefore * 3.0 + 0.5).Should().BeTrue(
                $"etaAfter={etaAfter} should not blow up from etaBefore={etaBefore}");
        }

        [Fact]
        public async Task AverageCandidate_KicksIn_When_PaceMissing()
        {
            // very thin total, cant learn too much
            var est = new EtaEstimator(totalUnits: 4);

            // 2 @ 30ms
            for (int i = 0; i < 2; i++)
            {
                await Task.Delay(30);
                est.Step(1);
            }

            var eta = est.GetEtaSeconds();
            // Expect ~ 2 * 0.03 = 0.06s
            AssertEx.Approximately(eta, expected: 0.06, relTol: 0.6, absTol: 0.06);
        }
    }

}
