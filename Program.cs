using System;
using System.Collections.Generic;

using Nni;

namespace cs
{
    class Program
    {
        static void Main(string[] args)
        {
            var space = new ParameterRange[] {
                ParameterRange.Numerical("_algo_", "batch", 16, 32, false, true),
                ParameterRange.Categorical("_algo_", "conv", new string[] {"2", "3", "5", "7"}),
                ParameterRange.Numerical("_algo_", "dropout", 0.5, 0.9, false, false),
                ParameterRange.Numerical("_algo_", "lr", 0.0001, 0.1, true, false),
                ParameterRange.Numerical("_algo_", "hidden", 32, 1024, true, true),
            };

            var initConfig = new FlamlParameters();
            initConfig["batch"] = 32;
            initConfig["conv"] = 1;
            initConfig["dropout"] = 0.5;
            initConfig["lr"] = 0.001;
            initConfig["hidden"] = 128;

            var tuner = new FlamlTuner(space, initConfig);

            var rng = FlamlTuner.rng;

            FlamlParameters param = null;

            for (int i = 0; i < 10; i++) {
                Console.WriteLine($"===== {i} =====");
                param = tuner.GenerateParameters(i);
                foreach (var (name, val) in param) {
                    Console.WriteLine($"{name}: {val}");
                }
                double time = rng.Uniform(0, 60);
                double loss = rng.Uniform(0, 1);
                tuner.ReceiveTrialResult(i, loss, time);
            }

            /*for (int i = 0; i < 20; i++) {
                int idx = 0;
                for (int j = 0; j < 5; j++) {
                    idx = i * 5 + j;
                    param = tuner.GenerateParameters(idx);
                }

                Console.WriteLine($"===== {idx} =====");
                foreach (var (algoName, algoParams) in param) {
                    Console.WriteLine(algoName);
                    foreach (var (key, val) in algoParams) {
                        Console.WriteLine($"    {key}: {val}");
                    }
                }

                for (int j = 0; j < 5; j++) {
                    idx = i * 5 + j;
                    double metric = rng.Uniform(0, 1);
                    tuner.ReceiveTrialResult(idx, metric);
                }
            }*/
        }
    }
}
