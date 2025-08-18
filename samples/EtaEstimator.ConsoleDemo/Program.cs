using System;
using System.Threading;
using EtaEstimator; // Namespace aus deiner Lib

namespace EtaEstimator.ConsoleDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== ETA Estimator Demo ===");

            // Beispiel: wir haben 50 "Arbeitseinheiten"
            var eta = new EtaEstimator(totalUnits: 50, maxDropPerTick: 1.5);

            var rnd = new Random();

            for (int i = 0; i < 50; i++)
            {
                // Simuliere Arbeit
                int workMs = rnd.Next(100, 400); // 0.1–0.4s
                Thread.Sleep(workMs);

                // Fortschritt melden
                double remainingSec = eta.Step();

                Console.WriteLine($"Step {i + 1}/50 | ETA: {remainingSec:F1} sec");
            }

            Console.WriteLine("Fertig! Alles erledigt.");
        }
    }
}