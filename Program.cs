using System;
using System.Collections.Generic;

using Nni;

namespace cs
{
    class Program
    {
        static void Main(string[] args)
        {
            var space = new Domain[] {
                Domain.Choice("conv_size", new string[] {"2", "3", "5", "7"}, "3"),
                Domain.Uniform("dropout_rate", 0.5, 0.9, 0.8),
                Domain.LogUniform("lr", 0.0001, 0.1, 0.001),
                Domain.RandInt("batch_size", 16, 32, 32),
                Domain.LogRandInt("hidden_size", 32, 1024, 128),
            };

            var searcher = new Flaml(space);

            for (int i = 0; i < 10; i++) {
                Console.WriteLine($"===== {i} =====");

                Parameters param = searcher.GenerateParameters(i);

                foreach (var (name, val) in param) {
                    Console.WriteLine($"{name}: {val}");
                }
                double time = Flaml.rng.Uniform(0, 60);
                double loss = Flaml.rng.Uniform(0, 1);

                searcher.ReceiveTrialResult(i, loss, time);
            }
        }
    }
}
