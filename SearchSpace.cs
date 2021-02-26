using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace Nni
{
    class Parameters : Dictionary<string, AlgorithmParameters> { }

    class AlgorithmParameters : Dictionary<string, string> { }


    class SearchSpace
    {
        // in example: { 'A': search_space_A, 'B': search_space_B }
        public Dictionary<string, AlgorithmSpace> algorithms = new Dictionary<string, AlgorithmSpace>();

        // in example: [ ['A','B','C','F','G'], ['A','B','C','F','H'], ... ]
        public List<List<string>> pipelines = new List<List<string>>();

        public SearchSpace(string jsonString)
        {
            foreach (var pipelineJson in JArray.Parse(jsonString)) {
                var pipeline = new List<string>();
                pipelines.Add(pipeline);

                foreach (var algoKV in (JObject)pipelineJson) {
                    pipeline.Add(algoKV.Key);

                    var algo = new AlgorithmSpace();
                    algo.name = algoKV.Key;
                    algorithms[algo.name] = algo;

                    foreach (var paramKV in (JObject)algoKV.Value) {
                        string paramName = paramKV.Key;
                        var paramJson = (JObject)paramKV.Value;

                        string type = (string)paramJson["_type"];

                        if (type == "choice") {
                            var values = new List<string>();
                            foreach (var val in (JArray)paramJson["_value"]) {
                                values.Add((string)val);
                            }
                            algo.Add(ParameterRange.Categorical(algo.name, paramName, values.ToArray()));

                        } else {
                            JArray values = (JArray)paramJson["_value"];
                            double low = (double)values[0];
                            double high = (double)values[1];

                            bool log = (type == "loguniform" || type == "qloguniform");
                            bool integer = (type == "quniform" || type == "qloguniform");

                            algo.Add(ParameterRange.Numerical(algo.name, paramName, low, high, log, integer));
                        }
                    }
                }
            }
        }
    }

    class AlgorithmSpace : List<ParameterRange>
    {
        public string name;
    }

    class ParameterRange
    {
        public string name;
        public string tag;

        public bool isCategorical;

        // categorical
        public int size;
        public string[] categoricalValues;

        // numerical
        public double low;
        public double high;
        public bool isLogDistributed;
        public bool isInteger;

        public static ParameterRange Categorical(string algorithmName, string name, string[] values)
        {
            return new ParameterRange(
                name, algorithmName, true,
                values.Length, values,
                Double.NaN, Double.NaN, false, false
            );
        }

        public static ParameterRange Numerical(
            string algorithmName, string name,
            double low, double high, bool isLogDistributed = false, bool isInteger = false)
        {
            return new ParameterRange(
                name, algorithmName, false,
                -1, null,
                low, high, isLogDistributed, isInteger
            );
        }

        private ParameterRange(
            string name, string algorithmName, bool isCategorical,
            int size, string[] categoricalValues,
            double low, double high, bool isLogDistributed, bool isInteger)
        {
            this.name = name;
            this.tag = algorithmName + '|' + name;

            this.isCategorical = isCategorical;

            this.size = size;
            this.categoricalValues = categoricalValues;

            this.low = low;
            this.high = high;
            this.isLogDistributed = isLogDistributed;
            this.isInteger = isInteger;

            if (isLogDistributed) {
                this.high = Math.Log(this.high);
                this.low = Math.Log(this.low);
            }
        }
    }
}
