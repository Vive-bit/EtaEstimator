// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Tests
{
    using System.Threading.Tasks;
    using Xunit;
    using Helper;
    using FluentAssertions;

    public class BasicBehaviorTests
    {
        [Fact]
        public void Starts_With_Infinite_ETA()
        {
            var est = new EtaEstimator(totalUnits: 10);
            var eta = est.GetEtaSeconds();
            eta.Should().Be(double.PositiveInfinity);
        }

        [Fact]
        public async Task Converges_On_Stable_Pace()
        {
            var est = new EtaEstimator(totalUnits: 20);

            // Simuliere konstante 50ms pro Item
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(50);
                est.Step(1);
            }

            // noch 10 Einheiten offen, erwartete ETA ~ 10 * 0.05 = 0.5s
            var etaNow = est.GetEtaSeconds();
            AssertEx.Approximately(etaNow, expected: 0.5, relTol: 0.40, absTol: 0.35);
        }

        [Fact]
        public async Task Percent_Grows_And_Reaches_100()
        {
            var est = new EtaEstimator(totalUnits: 5);

            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(10);
                est.Step(1);
            }

            est.Done.Should().Be(5);
            est.Total.Should().Be(5);
            est.Percent.Should().BeApproximately(100.0, 1e-9);
            est.GetEtaSeconds().Should().Be(0.0);
        }
    }

}
