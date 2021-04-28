using System;
using System.Collections.Generic;
using System.Linq;

namespace Nni
{
    class FlamlParameters : Dictionary<string, double>
    {
        public FlamlParameters() { }
        public FlamlParameters(FlamlParameters p) : base(p) { }
    }

    class FlamlTuner
    {
        private const double stepSize = 0.1;
        private const double stepLowerBound = 0.0001;

        public static RandomNumberGenerator rng = new RandomNumberGenerator();

        private ParameterRange[] space;
        private bool minimize;

        private int numComplete4Incumbent = 0;
        private double costComplete4Incumbent = 0;
        private double? costIncumbent = null;
        private FlamlParameters incumbent;
        private int dim;
        private int numAllowed4Incumbent = 0;
        private double[] directionTried = null;
        private double step;
        private Dictionary<int, FlamlParameters> proposedBy = new Dictionary<int, FlamlParameters>();
        private Dictionary<int, FlamlParameters> configs = new Dictionary<int, FlamlParameters>();
        private int trialCount = 1;
        private double? bestObj = null;
        private FlamlParameters bestConfig;
        private int k = 0;
        private int oldK = 0;
        private double stepUpperBound;
        private int iterBestConfig = 1;

        public FlamlTuner(ParameterRange[] searchSpace, FlamlParameters initConfig, bool minimizeMode = true)
        {
            this.space = searchSpace;
            this.minimize = minimizeMode;

            this.bestConfig = initConfig;
            this.incumbent = this.Normalize(initConfig);
            this.dim = searchSpace.Length;
            this.numAllowed4Incumbent = 2 * this.dim;
            this.step = stepSize * Math.Sqrt(this.dim);
            this.stepUpperBound = Math.Sqrt(dim);
            if (this.step > this.stepUpperBound) {
                this.step = this.stepUpperBound;
            }
        }

        private FlamlParameters Normalize(FlamlParameters config)
        {
            var normal = new FlamlParameters();
            foreach (var range in this.space) {
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
            foreach (var range in this.space) {
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

        public FlamlParameters GenerateParameters(int trialId)
        {
            this.numAllowed4Incumbent -= 1;
            var move = new FlamlParameters(incumbent);
            if (this.directionTried != null) {
                for (int i = 0; i < this.space.Length; i++) {
                    move[this.space[i].name] -= this.directionTried[i];
                }
                this.directionTried = null;
            }
            this.directionTried = this.RandVectorSphere();
            for (int i = 0; i < this.space.Length; i++) {
                move[this.space[i].name] += this.directionTried[i];
            }
            this.Project(move);
            var config = this.Denormalize(move);
            this.proposedBy[trialId] = this.incumbent;
            this.configs[trialId] = config;
            return config;
        }

        private double[] RandVectorSphere()
        {
            double[] vec = rng.Normal(0, 1, this.dim);
            double mag = ArrayMath.Norm(vec);
            return ArrayMath.Mul(vec, this.step / mag);
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
