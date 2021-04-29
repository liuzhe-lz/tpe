using System;
using System.Collections.Generic;
using System.Linq;

namespace Nni
{
    class FlamlParameters : Dictionary<string, double>
    {
        public FlamlParameters() { }
        public FlamlParameters(FlamlParameters copy) : base(copy) { }
    }

    class Flaml
    {
        private const double stepSize = 0.1;
        private const double stepLowerBound = 0.0001;

        public static RandomNumberGenerator rng = new RandomNumberGenerator();

        private Domain[] space;
        private bool minimize;

        private FlamlParameters bestConfig;
        private double? bestObj = null;
        private Dictionary<int, FlamlParameters> configs = new Dictionary<int, FlamlParameters>();
        private double costComplete4Incumbent = 0;
        private double? costIncumbent = null;
        private int dim;
        private double[] directionTried = null;
        private FlamlParameters incumbent;
        private int iterBestConfig = 1;
        private int k = 0;
        private int numAllowed4Incumbent = 0;
        private int numComplete4Incumbent = 0;
        private int oldK = 0;
        private Dictionary<int, FlamlParameters> proposedBy = new Dictionary<int, FlamlParameters>();
        private double step;
        private double stepUpperBound;
        private int trialCount = 1;

        public Flaml(Domain[] searchSpace, bool minimizeMode = true)
        {
            this.space = searchSpace;
            this.minimize = minimizeMode;

            bestConfig = GetInitialParameters();
            incumbent = Normalize(bestConfig);
            dim = space.Length;
            numAllowed4Incumbent = 2 * dim;
            step = stepSize * Math.Sqrt(dim);
            stepUpperBound = Math.Sqrt(dim);
            if (step > stepUpperBound) {
                step = stepUpperBound;
            }
        }

        public Parameters GenerateParameters(int trialId)
        {
            numAllowed4Incumbent -= 1;
            var move = new FlamlParameters(incumbent);
            if (directionTried != null) {
                for (int i = 0; i < space.Length; i++) {
                    move[space[i].name] -= directionTried[i];
                }
                directionTried = null;
            }
            directionTried = RandVectorSphere();
            for (int i = 0; i < space.Length; i++) {
                move[space[i].name] += directionTried[i];
            }
            Project(move);
            var config = Denormalize(move);
            proposedBy[trialId] = incumbent;
            configs[trialId] = config;

            var ret = new Parameters();
            foreach (var range in space) {
                string key = range.name;
                double val = config[key];
                if (range.isCategorical) {
                    ret[key] = range.categoricalValues[(int)Math.Round(val)];
                } else if (range.isInteger) {
                    ret[key] = ((int)Math.Round(val)).ToString();
                } else {
                    ret[key] = val.ToString();
                }
            }
            return ret;
        }

        public void ReceiveTrialResult(int trialId, double metric, double cost)
        {
            trialCount += 1;
            double obj = minimize ? metric : -metric;
            if (bestObj == null || obj < bestObj) {
                bestObj = obj;
                bestConfig = configs[trialId];
                incumbent = Normalize(bestConfig);
                costIncumbent = cost;
                numComplete4Incumbent = 0;
                costComplete4Incumbent = 0;
                numAllowed4Incumbent = 2 * dim;
                proposedBy.Clear();
                if (k > 0) {
                    step *= Math.Sqrt(k / oldK);
                }
                if (step > stepUpperBound) {
                    step = stepUpperBound;
                }
                iterBestConfig = trialCount;
                return;
            }
            if (proposedBy[trialId] == incumbent) {
                numComplete4Incumbent += 1;
                costComplete4Incumbent += cost;
                if (numComplete4Incumbent >= 2 * dim && numAllowed4Incumbent == 0) {
                    numAllowed4Incumbent = 2;
                }
                if (numComplete4Incumbent == 1 << dim) {
                    if (step >= StepLowerBound()) {
                        oldK = k != 0 ? k : iterBestConfig;
                        k = trialCount + 1;
                        step *= Math.Sqrt(oldK / k);
                    }
                    numComplete4Incumbent -= 2;
                    if (numAllowed4Incumbent < 2) {
                        numAllowed4Incumbent = 2;
                    }
                }
            }
        }

        private FlamlParameters GetInitialParameters()
        {
            var param = new FlamlParameters();
            foreach (var range in space) {
                param[range.name] = range.initialValue;
            }
            return param;
        }

        private FlamlParameters Normalize(FlamlParameters config)
        {
            var normal = new FlamlParameters();
            foreach (var range in space) {
                string key = range.name;
                double val = config[key];
                if (range.isCategorical) {
                    normal[key] = (val + 0.5) / range.size;
                } else if (range.isLogDistributed) {
                    normal[key] = Math.Log(val / range.low) / Math.Log(range.high / range.low);
                } else {
                    normal[key] = (val - range.low) / (range.high - range.low);
                }
            }
            return normal;
        }

        private FlamlParameters Denormalize(FlamlParameters config)
        {
            var denormal = new FlamlParameters();
            foreach (var range in space) {
                string key = range.name;
                double val = config[key];
                if (range.isCategorical) {
                    denormal[key] = Math.Floor(val * range.size);
                } else if (range.isLogDistributed) {
                    denormal[key] = Math.Pow(range.high / range.low, val) * range.low;
                } else {
                    denormal[key] = val * (range.high - range.low) + range.low;
                }
            }
            return denormal;
        }

        private double[] RandVectorSphere()
        {
            double[] vec = rng.Normal(0, 1, dim);
            double mag = ArrayMath.Norm(vec);
            return ArrayMath.Mul(vec, step / mag);
        }

        private void Project(FlamlParameters config)
        {
            foreach (var (key, val) in config) {
                if (val < 0) {
                    config[key] = 0;
                } else if (val > 1) {
                    config[key] = 1;
                }
            }
        }

        private double StepLowerBound()
        {
            foreach (var range in space) {
                if (range.isInteger && range.isLogDistributed) {
                    double x = Math.Log(1.0 + 1.0 / bestConfig[range.name]) / Math.Log(range.high / range.low);
                    return x * Math.Sqrt(dim);
                }
            }
            return stepLowerBound;
        }
    }
}
