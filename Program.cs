using System;
using System.Collections.Generic;

using Nni;

namespace cs
{
    class Program
    {
        static void Main(string[] args)
        {
            string searchSpaceJson = @"
                [
                    {
                        'A': {
                            'A_param1': { '_type': 'choice', '_value': [ 'x', 'y' ] },
                            'A_param2': { '_type': 'uniform', '_value': [ 0, 1 ] }
                        },
                        'B': {
                            'B_param': { '_type': 'loguniform', '_value': [ 0.0001, 0.1 ] }
                        },
                        'C': {
                            'C_param': { '_type': 'quniform', '_value': [ 16, 32 ] }
                        },
                        'F': {
                            'F_param': { '_type': 'qloguniform', '_value': [ 32, 1024 ] }
                        },
                        'G': {
                            'G_param': { '_type': 'choice', '_value': [ 'g1', 'g2' ] }
                        }
                    },
                    {
                        'A': { 'A_param1': { '_type': 'choice', '_value': [ 'x', 'y' ] }, 'A_param2': { '_type': 'uniform', '_value': [ 0, 1 ] } },
                        'B': { 'B_param': { '_type': 'loguniform', '_value': [ 0.0001, 0.1 ] } },
                        'C': { 'C_param': { '_type': 'quniform', '_value': [ 16, 32 ] } },
                        'F': { 'F_param': { '_type': 'qloguniform', '_value': [ 32, 1024 ] } },
                        'H': { 'H_param': { '_type': 'choice', '_value': [ 'h1', 'h2' ] } }
                    },
                    {
                        'A': { 'A_param1': { '_type': 'choice', '_value': [ 'x', 'y' ] }, 'A_param2': { '_type': 'uniform', '_value': [ 0, 1 ] } },
                        'D': { 'D_param': { '_type': 'choice', '_value': [ 'd1', 'd2' ] } },
                        'F': { 'F_param': { '_type': 'qloguniform', '_value': [ 32, 1024 ] } },
                        'G': { 'G_param': { '_type': 'choice', '_value': [ 'g1', 'g2' ] } }
                    },
                    {
                        'A': { 'A_param1': { '_type': 'choice', '_value': [ 'x', 'y' ] }, 'A_param2': { '_type': 'uniform', '_value': [ 0, 1 ] } },
                        'D': { 'D_param': { '_type': 'choice', '_value': [ 'd1', 'd2' ] } },
                        'F': { 'F_param': { '_type': 'qloguniform', '_value': [ 32, 1024 ] } },
                        'H': { 'H_param': { '_type': 'choice', '_value': [ 'h1', 'h2' ] } }
                    }
                ]";

            var space = new SearchSpace(searchSpaceJson);
            var tuner = new TpeTuner(space);

            var rng = TpeTuner.rng;

            Parameters param = null;

            for (int i = 0; i < 20; i++) {
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
            }
        }
    }
}
