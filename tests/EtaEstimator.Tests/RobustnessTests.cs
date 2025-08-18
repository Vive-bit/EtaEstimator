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

            // 20 Items stabil @ 20ms
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(20);
                est.Step(1);
            }

            var etaBefore = est.GetEtaSeconds();

            // Ausreißer: eine lange Wartezeit
            await Task.Delay(400);
            est.Step(1);

            // Danach wieder normal
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(20);
                est.Step(1);
            }

            var etaAfter = est.GetEtaSeconds();

            // Erwartung: kein massiver Sprung; ETA darf ansteigen, aber begrenzt.
            etaAfter.Should().BeGreaterThan(0);
            // nicht mehr als Faktor 3 gegenüber vorher (großzügig)
            (etaAfter <= etaBefore * 3.0 + 0.5).Should().BeTrue(
                $"etaAfter={etaAfter} should not blow up from etaBefore={etaBefore}");
        }

        [Fact]
        public async Task AverageCandidate_KicksIn_When_PaceMissing()
        {
            // sehr kleines total, der Filter kann am Anfang noch nicht viel lernen
            var est = new EtaEstimator(totalUnits: 4);

            // zwei stabile Schritte
            for (int i = 0; i < 2; i++)
            {
                await Task.Delay(30);
                est.Step(1);
            }

            var eta = est.GetEtaSeconds();
            // Erwartung ~ 2 * 0.03 = 0.06s
            AssertEx.Approximately(eta, expected: 0.06, relTol: 0.6, absTol: 0.06);
        }
    }

}
