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

            // Ein paar langsame Schritte -> ETA relativ hoch
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(80);
                est.Step(1);
            }
            var before = est.GetEtaSeconds();

            // Jetzt sehr schnelle Schritte, ohne Smoothing würde ETA stark fallen
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(5);
                var eta = est.Step(1); // pro Tick darf nur 0.20 s fallen
                if (!double.IsInfinity(before))
                    (before - eta <= 0.20 + 1e-9).Should().BeTrue();
                before = eta;
            }
        }
    }

}
