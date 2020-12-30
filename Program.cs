using System;
using System.Collections.Generic;

using Nni;

namespace cs
{
    class Program
    {
        static void Main(string[] args)
        {
            string searchSpace = @"
                [
                    {
                        'model1': {
                            'batch_size': { '_type': 'quniform', '_value': [ 16, 32 ] },
                            'conv_size': { '_type': 'choice', '_value': [ 4 ] },
                            'dropout_rate': { '_type': 'uniform', '_value': [ 0.5, 0.9 ] },
                            'hidden_size': { '_type': 'qloguniform', '_value': [ 32, 1024 ] },
                            'learning_rate': { '_type': 'loguniform', '_value': [ 0.0001, 0.1 ] }
                        },
                        'model2': {
                            'test1': { '_type': 'choice', '_value': [ 2 ] },
                            'test2': { '_type': 'uniform', '_value': [ 0, 1 ] }
                        }
                    }
                ]
            ";

            var tuner = new TpeTuner(searchSpace);
            var rng = TpeTuner.rng;

            Parameter param = null;

            for (int i = 0; i < 10; i++) {
                int idx = 0;
                for (int j = 0; j < 5; j++) {
                    idx = i * 5 + j;
                    param = tuner.GenerateParameters(idx);
                }

                Console.WriteLine($"===== {idx} =====");
                foreach (var (key, val) in param) {
                    Console.WriteLine($"{key}: {val}");
                }

                for (int j = 0; j < 5; j++) {
                    idx = i * 5 + j;
                    double metric = rng.uniform(0, 1);
                    tuner.ReceiveTrialResult(idx, metric);
                }
            }
        }
    }
}
