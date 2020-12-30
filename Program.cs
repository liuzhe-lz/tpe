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
                        '0_0': {},
                        '0_1': {}
                    },
                    {
                        '1_0': {}
                    },
                    {
                        'model1': {
                            'batch_size': { '_type': 'quniform', '_value': [ 16, 32 ] },
                                'conv_size': { '_type': 'choice', '_value': [ 2, 3, 5, 7 ] },
                                'dropout_rate': { '_type': 'uniform', '_value': [ 0.5, 0.9 ] },
                                'hidden_size': { '_type': 'qloguniform', '_value': [ 32, 1024 ] },
                                'learning_rate': { '_type': 'loguniform', '_value': [ 0.0001, 0.1 ] }
                        },
                        'model2': {
                            'test1': { '_type': 'choice', '_value': [ 'a', 'b' ] },
                            'test2': { '_type': 'uniform', '_value': [ 0, 1 ] }
                        }
                    }
                ]
            ";

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
                for (int k = 0; k < param.Count; k++) {
                    Console.WriteLine($"{k}: {param[k].algorithmName}");
                    foreach (var (key, val) in param[k].parameters) {
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
