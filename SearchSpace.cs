using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace Nni
{
    class Parameters : List<PipeParameters> { }

    class PipeParameters {
        public string algorithmName;
        public Dictionary<string, string> parameters = new Dictionary<string, string>();
    }


    class SearchSpace : List<PipeSpace>
    {
        public SearchSpace(string jsonString)
        {
            int pipeIndex = -1;
            foreach (var pipeJson in JArray.Parse(jsonString)) {
                pipeIndex += 1;
                var pipe = new PipeSpace();
                Add(pipe);

                foreach (var algoKV in (JObject)pipeJson) {
                    var algo = new AlgorithmSpace();
                    pipe.Add(algo);
                    algo.name = algoKV.Key;

                    foreach (var paramKV in (JObject)algoKV.Value) {
                        string paramName = paramKV.Key;
                        var paramJson = (JObject)paramKV.Value;

                        string type = (string)paramJson["_type"];

                        if (type == "choice") {
                            var values = new List<string>();
                            foreach (var val in (JArray)paramJson["_value"]) {
                                values.Add((string)val);
                            }
                            algo.Add(ParameterRange.Categorical(pipeIndex, algo.name, paramName, values.ToArray()));

                        } else {
                            JArray values = (JArray)paramJson["_value"];
                            double low = (double)values[0];
                            double high = (double)values[1];

                            bool log = (type == "loguniform" || type == "qloguniform");
                            bool integer = (type == "quniform" || type == "qloguniform");

                            algo.Add(ParameterRange.Numerical(pipeIndex, algo.name, paramName, low, high, log, integer));
                        }
                    }
                }
            }
        }
    }

    class PipeSpace : List<AlgorithmSpace> { }

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

        public static ParameterRange Categorical(int pipeIndex, string algorithmName, string name, string[] values)
        {
            return new ParameterRange(
                name, pipeIndex, algorithmName, true,
                values.Length, values,
                Double.NaN, Double.NaN, false, false
            );
        }

        public static ParameterRange Numerical(
            int pipeIndex, string algorithmName, string name,
            double low, double high, bool isLogDistributed = false, bool isInteger = false)
        {
            return new ParameterRange(
                name, pipeIndex, algorithmName, false,
                -1, null,
                low, high, isLogDistributed, isInteger
            );
        }

        private ParameterRange(
            string name, int pipeIndex, string algorithmName, bool isCategorical,
            int size, string[] categoricalValues,
            double low, double high, bool isLogDistributed, bool isInteger)
        {
            this.name = name;
            this.tag = pipeIndex.ToString() + '|' + algorithmName + '|' + name;

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
