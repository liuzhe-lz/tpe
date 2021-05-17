using System;
using System.Collections.Generic;
using System.Linq;

namespace Nni
{
    class FlamlParameters : Dictionary<string, double>
    {
        public FlamlParameters() { }
        public FlamlParameters(FlamlParameters copy) : base(copy) { }

        public Parameters ToParameters(Domain[] searchSpace)
        {
            var ret = new Parameters();
            foreach (var range in searchSpace) {
                string key = range.name;
                double val = this[key];
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
    }

    class SearchThread
    {
        private const double eps = 1e-10;

        private Flow2 searchAlg;

        private double costBest;
        private double costBest1;
        private double costBest2;
        private double costLast;
        private double costTotal;
        private double objBest1;
        private double objBest2;
        private double speed = 0;

        public SearchThread(Flow2 searchAlgorithm = null)
        {
            this.searchAlg = searchAlgorithm;
            if (searchAlgorithm == null) {
                return;
            }

            costLast = searchAlgorithm.costIncumbent == null ? 0 : (double)searchAlgorithm.costIncumbent;
            costTotal = costLast;
            costBest = costLast;
            costBest1 = costLast;
            costBest2 = 0;
            objBest1 = searchAlgorithm.bestObj == null ? Double.PositiveInfinity : (double)searchAlgorithm.bestObj;
            objBest2 = objBest1;
        }

        public void OnTrialComplete(int trialId, double metric, double cost)
        {
            if (searchAlg == null) {
                return;
            }
            searchAlg.ReceiveTrialResult(trialId, metric, cost);
            costLast = cost;
            costTotal += cost;
            if (metric < objBest1) {
                costBest2 = costBest1;
                costBest1 = costTotal;
                objBest2 = Double.IsInfinity(objBest1) ? metric : objBest1;
                objBest1 = metric;
                costBest = costLast;
            }

            if (objBest2 > objBest1) {
                speed = (objBest2 - objBest1) / (costTotal - costBest2 + eps);
            } else {
                speed = 0;
            }
        }

        public FlamlParameters Suggest(int trialId)
        {
            return this.searchAlg.Suggest(trialId);
        }
    }

    class Flaml
    {
        private Domain[] space;
        private bool minimize;

        private Dictionary<int, FlamlParameters> configs = new Dictionary<int, FlamlParameters>();
        private FlamlParameters gsAdmissibleMax;
        private FlamlParameters gsAdmissibleMin;
        private bool initUsed = false;
        private Flow2 localSearch;
        private FlamlParameters lsBoundMax;
        private FlamlParameters lsBoundMin;
        private Dictionary<int, SearchThread> searchThreadPool = new Dictionary<int, SearchThread>();
        private int threadCount;
        private Dictionary<int, int> trialProposedBy = new Dictionary<int, int>();

        public Flaml(Domain[] searchSpace, bool minimizeMode = true)
        {
            this.space = searchSpace;
            this.minimize = minimizeMode;

            localSearch = new Flow2(searchSpace);
            searchThreadPool[0] = new SearchThread();
            threadCount = 1;
            lsBoundMin = localSearch.Normalize(localSearch.initConfig);
            lsBoundMax = new FlamlParameters(lsBoundMin);
            gsAdmissibleMin = new FlamlParameters(lsBoundMin);
            gsAdmissibleMax = new FlamlParameters(lsBoundMax);
        }

        public void ReceiveTrialResult(int trialId, double metric, double cost)
        {
            metric = minimize ? metric : -metric;
            int threadId = trialProposedBy[trialId];
            searchThreadPool[threadId].OnTrialComplete(trialId, metric, cost);
            if (threadId == 0 && searchThreadPool.Count < 2) {
                searchThreadPool[threadCount] = localSearch.CreateSearchThread(configs[trialId], metric, cost);
                threadId = threadCount;
                threadCount += 1;
                UpdateAdmissibleRegion(configs[trialId]);
            }
            lsBoundMin.ToList().ForEach(item => { gsAdmissibleMin[item.Key] = item.Value; });
            lsBoundMax.ToList().ForEach(item => { gsAdmissibleMax[item.Key] = item.Value; });
        }

        public Parameters GenerateParameters(int trialId)
        {
            if (searchThreadPool.Count < 2) {
                initUsed = false;
            }
            if (initUsed) {
                configs[trialId] = searchThreadPool[1].Suggest(trialId);
                trialProposedBy[trialId] = 1;
                UpdateAdmissibleRegion(configs[trialId]);
                lsBoundMin.ToList().ForEach(item => { gsAdmissibleMin[item.Key] = item.Value; });
                lsBoundMax.ToList().ForEach(item => { gsAdmissibleMax[item.Key] = item.Value; });
            } else {
                configs[trialId] = localSearch.initConfig;
                trialProposedBy[trialId] = 0;
                initUsed = true;
            }
            return configs[trialId].ToParameters(space);
        }

        private void UpdateAdmissibleRegion(FlamlParameters config)
        {
            var normalizedConfig = localSearch.Normalize(config);
            foreach (string key in lsBoundMin.Keys) {
                double val = normalizedConfig[key];
                if (val < lsBoundMin[key]) {
                    lsBoundMin[key] = val;
                } else if (val > lsBoundMax[key]) {
                    lsBoundMax[key] = val;
                }
            }
        }
    }

    class Flow2
    {
        private const double stepSize = 0.1;
        private const double stepLowerBound = 0.0001;

        public static RandomNumberGenerator rng = new RandomNumberGenerator();

        public double? bestObj = null;
        public double? costIncumbent = null;
        public FlamlParameters initConfig;

        private Domain[] space;
        private bool minimize;

        private FlamlParameters bestConfig;
        private Dictionary<int, FlamlParameters> configs = new Dictionary<int, FlamlParameters>();
        private double costComplete4Incumbent = 0;
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

        public Flow2(Domain[] searchSpace, bool minimizeMode = true)
        {
            this.space = searchSpace;
            this.minimize = minimizeMode;

            initConfig = GetInitialParameters();
            bestConfig = initConfig;
            incumbent = Normalize(bestConfig);
            dim = space.Length;
            numAllowed4Incumbent = 2 * dim;
            step = stepSize * Math.Sqrt(dim);
            stepUpperBound = Math.Sqrt(dim);
            if (step > stepUpperBound) {
                step = stepUpperBound;
            }
        }

        public SearchThread CreateSearchThread(FlamlParameters config, double metric, double cost)
        {
            var flow2 = new Flow2(space, minimize);
            flow2.bestObj = metric;
            flow2.costIncumbent = cost;
            return new SearchThread(flow2);
        }

        public FlamlParameters Suggest(int trialId)
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
            return config;
        }

        public Parameters GenerateParameters(int trialId)
        {
            return Suggest(trialId).ToParameters(space);
        }

        public void ReceiveTrialResult(int trialId, double metric, double cost)
        {
            trialCount += 1;
            double obj = metric;  // flipped in BlendSearch
            //double obj = minimize ? metric : -metric;
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

        public FlamlParameters Normalize(FlamlParameters config)
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
