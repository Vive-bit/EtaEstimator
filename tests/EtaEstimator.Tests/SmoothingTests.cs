// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.Tests
{
    using System.Threading.Tasks;
    using Xunit;
    using FluentAssertions;

    public class SmoothingTests
    {
        [Fact]
        public async Task MaxDropPerTick_Is_Respected()
        {
            var est = new EtaEstimator(totalUnits: 50, maxDropPerTick: 0.20);

            // A few slow steps > ETA prob. really high
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(80);
                est.Step(1);
            }
            var before = est.GetEtaSeconds();

            // Now 2 fast steps, ETA would fall without smoothing
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(5);
                var eta = est.Step(1); // per tick 0.20s max
                if (!double.IsInfinity(before))
                    (before - eta <= 0.20 + 1e-9).Should().BeTrue();
                before = eta;
            }
        }
    }

}
