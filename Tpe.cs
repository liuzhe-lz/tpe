using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace Nni {

    class SearchRange
    {
        public int groupIdx;
        public int size;
        public double low;
        public double high;
        public bool categorical;
        public bool log;
        public bool integer;

        public static SearchRange Categorical(int groupIdx, int n)
        {
            return new SearchRange(groupIdx, n, Double.NaN, Double.NaN, true, false, false);
        }

        public static SearchRange Numerical(int groupIdx, double low, double high, bool log, bool integer)
        {
            return new SearchRange(groupIdx, -1, low, high, false, log, integer);
        }

        public SearchRange(int groupIdx, int size, double low, double high, bool categorical, bool log, bool integer)
        {
            this.groupIdx = groupIdx;
            this.size = size;
            this.low = low;
            this.high = high;
            this.categorical = categorical;
            this.log = log;
            this.integer = integer;

            if (log) {
                this.high = Math.Log(this.high);
                this.low = Math.Log(this.low);
            }
        }
    }

    class Parameter : Dictionary<string, double>
    {
    }

    class Result
    {
        public int id;
        public double loss;
        public Parameter param;

        public Result(int id, double loss, Parameter param)
        {
            this.id = id;
            this.loss = loss;
            this.param = param;
        }
    }

    class RandomGenerator
    {
        //private long a = 1103515245;
        //private long c = 12345;
        //private long m = (long)1 << 31;
        //public long seed;

        private Random rnd = new Random();

        public RandomGenerator(int seed = 0)
        {
            //this.seed = seed;
        }

        //private long rand()
        //{
        //    seed = (a * seed + c) % m;
        //    return seed;
        //}

        public int randint(int high)
        {
            return rnd.Next(high);
            //return (int)(rand() % high);
        }

        public double uniform(double low, double high)
        {
            return rnd.NextDouble() * (high - low) + low;
            //return (double)rand() / m * (high - low) + low;
        }

        public double normal(double loc, double scale)
        {
            double u = 1 - uniform(0, 1);
            double v = 1 - uniform(0, 1);
            double std = Math.Sqrt(-2.0 * Math.Log(u)) * Math.Sin(2.0 * Math.PI * v);
            return loc + std * scale;
        }

        public int Categorical(double[] possibility)
        {
            double x = uniform(0, 1);
            for (int i = 0; i < possibility.Length; i++) {
                x -= possibility[i];
                if (x < 0) { return i; }
            }
            return possibility.Length - 1;
        }
    }

    class TpeTuner
    {
        private const int nStartupJobs = 20;
        private const double priorWeight = 1.0;
        private const double olossGamma = 0.25;
        private const int lf = 25;
        private const int nEiCandidates = 24;
        private const double eps = 1e-12;

        public static RandomGenerator rng = new RandomGenerator(0);

        private Dictionary<string, SearchRange> space;
        private int nGroup;
        private bool minimize;
        private Dictionary<int, Parameter> paramHistory = new Dictionary<int, Parameter>();
        private HashSet<int> running = new HashSet<int>();
        private List<Result> results = new List<Result>();
        private double lie = Double.PositiveInfinity;

        private static void print(double num)
        {
            Console.WriteLine($"{num}");
        }

        private static void print(double[] array)
        {
            Console.WriteLine(string.Join(" ", array));
        }

        private static void cprint(bool cond, double val) { if (cond) print(val); }
        private static void cprint(bool cond, double[] val) { if (cond) print(val); }

        public TpeTuner(string jsonSearchSpace, bool minimizeMode = true)
        {
            JArray spaceArray = JArray.Parse(jsonSearchSpace);
            JObject spaceJson = (JObject)spaceArray[0];

            var space = new Dictionary<string, SearchRange>();
            int groupIndex = 0;
            foreach (var groupKV in spaceJson) {
                foreach (var rangeKV in (JObject)groupKV.Value) {
                    string tag = rangeKV.Key;
                    JObject rangeJson = (JObject)rangeKV.Value;
                    string type = (string)rangeJson["_type"];
                    JArray values = (JArray)rangeJson["_value"];
                    if (type == "uniform") {
                        space[tag] = SearchRange.Numerical(groupIndex, (double)values[0], (double)values[1], false, false);
                    } else if (type == "loguniform") {
                        space[tag] = SearchRange.Numerical(groupIndex, (double)values[0], (double)values[1], true, false);
                    } else if (type == "quniform") {
                        space[tag] = SearchRange.Numerical(groupIndex, (double)values[0], (double)values[1], false, true);
                    } else if (type == "qloguniform") {
                        space[tag] = SearchRange.Numerical(groupIndex, (double)values[0], (double)values[1], true, true);
                    } else if (type == "choice") {
                        space[tag] = SearchRange.Categorical(groupIndex, (int)values[0]);
                    }
                }
                groupIndex += 1;
            }

            this.nGroup = groupIndex;
            this.space = space;
            this.minimize = minimizeMode;
        }

        public TpeTuner(int nGroup, Dictionary<string, SearchRange> searchSpace, bool minimizeMode = true)
        {
            this.nGroup = nGroup;
            this.space = searchSpace;
            this.minimize = minimizeMode;
        }

        public Parameter GenerateParameters(int paramId)
        {
            Parameter param;
            if (paramHistory.Count > nStartupJobs && running.Count > 0) {
                var fork = new List<Result>(results);
                foreach (int id in running) {
                    fork.Add(new Result(fork.Count, lie, paramHistory[id]));
                }
                param = TpeSuggest(fork);
            } else {
                param = TpeSuggest(results);
            }
            paramHistory[paramId] = param;
            running.Add(paramId);
            return param;
        }

        public void ReceiveTrialResult(int paramId, double loss)
        {
            if (!minimize) {
                loss = -loss;
            }
            running.Remove(paramId);
            lie = Math.Min(lie, loss);
            results.Add(new Result(paramId, loss, paramHistory[paramId]));
        }

        private Parameter TpeSuggest(List<Result> history)
        {
            if (history.Count < nStartupJobs) {
                return RandomSuggest();
            }
            var param = new Parameter();
            int groupIdx = Choice(history, "_group_", nGroup);

            param["_group_"] = groupIdx;
            foreach (var (tag, range) in space) {
                if (groupIdx != range.groupIdx) { continue; }
                if (range.categorical) {
                    param[tag] = Choice(history, tag, range.size);
                } else {
                    param[tag] = Uniform(history, tag, range.low, range.high, range.log, range.integer);
                }
            }

            //Console.WriteLine("####################");
            //foreach (var (key, val) in param) {
            //    Console.WriteLine($"{key}: {val}");
            //}
            //Environment.Exit(0);

            return param;
        }

        private Parameter RandomSuggest()
        {
            var param = new Parameter();
            int groupIdx = rng.randint(nGroup);
            param["_group_"] = groupIdx;
            foreach (var (tag, range) in space) {
                if (groupIdx != range.groupIdx) { continue; }
                if (range.categorical) {
                    param[tag] = rng.randint(range.size);
                } else {
                    double val = rng.uniform(range.low, range.high);
                    if (range.log) {
                        val = Math.Exp(val);
                    }
                    if (range.integer) {
                        val = Math.Round(val);
                    }
                    param[tag] = val;
                }
            }
            return param;
        }

        private static int Choice(List<Result> results, string tag, int size)
        {
            var (obsBelow, obsAbove) = ApSplitTrials(results, tag);

            double[] weights = LinearForgettingWeights(obsBelow.Length);
            double[] counts = Bincount(obsBelow, weights, size);
            double[] p = ArrayMath.Div(ArrayMath.Add(counts, priorWeight), ArrayMath.Sum(ArrayMath.Add(counts, priorWeight)));
            int[] sample = Categorical(p, nEiCandidates);
            double[] belowLLik = ArrayMath.Log(ArrayMath.Index(p, sample));

            weights = LinearForgettingWeights(obsAbove.Length);
            counts = Bincount(obsAbove, weights, size);
            p = ArrayMath.Div(ArrayMath.Add(counts, priorWeight), ArrayMath.Sum(ArrayMath.Add(counts, priorWeight)));
            double[] aboveLLik = ArrayMath.Log(ArrayMath.Index(p, sample));

            return FindBest(sample, belowLLik, aboveLLik);
        }

        private static double Uniform(List<Result> results, string tag, double low, double high, bool log, bool integer)
        {
            var (obsBelow, obsAbove) = ApSplitTrials(results, tag);

            if (log) {
                obsBelow = ArrayMath.Log(obsBelow);
                obsAbove = ArrayMath.Log(obsAbove);
            }

            double priorMu = 0.5 * (high + low);
            double priorSigma = high - low;

            var (weights, mus, sigmas) = AdaptiveParzenNormal(obsBelow, priorMu, priorSigma);
            double[] samples = Gmm1(weights, mus, sigmas, low, high, log, integer);
            double[] belowLLik = Gmm1Lpdf(samples, weights, mus, sigmas, low, high, log, integer);
            //cprint(log && integer, belowLLik);

            (weights, mus, sigmas) = AdaptiveParzenNormal(obsAbove, priorMu, priorSigma);
            double[] aboveLLik = Gmm1Lpdf(samples, weights, mus, sigmas, low, high, log, integer);

            return FindBest(samples, belowLLik, aboveLLik);
        }

        private static (double[], double[]) ApSplitTrials(List<Result> results, string tag)
        {
            int nBelow = Math.Min((int)Math.Ceiling(olossGamma * Math.Sqrt(results.Count)), lf);
            var sorted = results.OrderBy(result => result.loss);
            var below = sorted.Take(nBelow).Where(result => result.param.ContainsKey(tag));
            var above = sorted.Skip(nBelow).Where(result => result.param.ContainsKey(tag));
            var belowValue = below.OrderBy(result => result.id).Select(result => result.param[tag]).ToArray();
            var aboveValue = above.OrderBy(result => result.id).Select(result => result.param[tag]).ToArray();
            return (belowValue, aboveValue);
        }

        private static T FindBest<T>(T[] samples, double[] belowLLik, double[] aboveLLik)
        {
            int best = ArrayMath.ArgMax(ArrayMath.Sub(belowLLik, aboveLLik));
            return samples[best];
        }

        private static (double[], double[], double[]) AdaptiveParzenNormal(double[] mus, double priorMu, double priorSigma)
        {
            int[] order = ArrayMath.ArgSort(mus);
            double[] sortedMus = ArrayMath.Index(mus, order);
            int priorPos = ArrayMath.SearchSorted(sortedMus, priorMu);
            sortedMus = ArrayMath.Insert(sortedMus, priorPos, priorMu);

            int length = mus.Length;
            double[] sigma = new double[length + 1];

            if (length == 0) {
                sigma[0] = priorSigma;

            } else if (length == 1) {
                sigma[priorPos] = priorSigma;
                sigma[1 - priorPos] = priorSigma * 0.5;

            } else {
                sigma[0] = sortedMus[1] - sortedMus[0];
                sigma[length] = sortedMus[length] - sortedMus[length - 1];
                for (int i = 1; i < length; i++) {
                    sigma[i] = Math.Max(sortedMus[i] - sortedMus[i - 1], sortedMus[i + 1] - sortedMus[i]);
                }
            }

            double maxSigma = priorSigma / 1.0;
            double minSigma = priorSigma / Math.Min(100.0, length + 2);
            sigma = ArrayMath.Clip(sigma, minSigma, maxSigma);
            sigma[priorPos] = priorSigma;

            double[] sortedWeights;
            if (lf < length) {
                sortedWeights = ArrayMath.Index(LinearForgettingWeights(length), order);
            } else {
                sortedWeights = Enumerable.Repeat(1.0, length).ToArray();
            }
            sortedWeights = ArrayMath.Insert(sortedWeights, priorPos, priorWeight);
            sortedWeights = ArrayMath.DivSum(sortedWeights);

            return (sortedWeights, sortedMus, sigma);
        }

        private static int[] Categorical(double[] p, int size)
        {
            int[] ret = new int[size];
            for (int i = 0; i < ret.Length; i++) {
                ret[i] = rng.Categorical(p);
            }
            return ret;
        }

        private static double[] LinearForgettingWeights(int n)
        {
            double[] weights = Enumerable.Repeat(1.0, n).ToArray();
            double rampStart = 1.0 / n;
            int rampLength= n - lf;
            if (rampLength == 1) {
                weights[0] = rampStart;
                return weights;
            }
            for (int i = 0; i < rampLength; i++) {
                weights[i] = rampStart + (1.0 - rampStart) / (rampLength - 1) * i;
            }
            return weights;
        }

        private static double[] Gmm1(double[] weights, double[] mus, double[] sigmas, double low, double high, bool log, bool integer)
        {
            double[] samples = new double[nEiCandidates];
            for (int i = 0; i < nEiCandidates; i++) {
                while (true) {
                    int active = rng.Categorical(weights);
                    double draw = rng.normal(mus[active], sigmas[active]);
                    if (draw < low || draw >= high) { continue; }

                    if (log) { draw = Math.Exp(draw); }
                    if (integer) { draw = Math.Round(draw); }
                    samples[i] = draw;
                    break;
                }
            }
            return samples;
        }

        private static double[] Gmm1Lpdf(double[] samples, double[] weights, double[] mus, double[] sigmas, double low, double high, bool log, bool integer)
        {
            double pAccept = ArrayMath.Sum(ArrayMath.Mul(weights, ArrayMath.Sub(NormalCdf(high, mus, sigmas), NormalCdf(low, mus, sigmas))));

            double[] ret = new double[samples.Length];
            for (int i = 0; i < samples.Length; i++) {
                if (!integer) {
                    if (log) {
                        ret[i] = LogSum(LognormalLpdf(samples[i], weights, mus, sigmas));
                    } else {
                        ret[i] = LogSum(NormalLpdf(samples[i], weights, mus, sigmas, pAccept));
                    }
                } else {
                    double prob = 0;
                    double ubound;
                    double lbound;
                    if (log) {
                        ubound = Math.Log(Math.Min(samples[i] + 0.5, Math.Exp(high)));
                        lbound = Math.Log(Math.Max(samples[i] - 0.5, Math.Exp(low)));
                    } else {
                        ubound = Math.Min(samples[i] + 0.5, high);
                        lbound = Math.Max(samples[i] - 0.5, low);
                    }
                    prob += ArrayMath.Mul(weights, NormalCdf(ubound, mus, sigmas)).Sum();
                    prob -= ArrayMath.Mul(weights, NormalCdf(lbound, mus, sigmas)).Sum();
                    ret[i] = Math.Log(prob) - Math.Log(pAccept);
                }
            }
            return ret;
        }

        private static double[] NormalCdf(double x, double[] mu, double[] sigma)
        {
            double[] z = new double[mu.Length];
            for (int i = 0; i < z.Length; i++) {
                double top = x - mu[i];
                double bottom = Math.Max(Math.Sqrt(2) * sigma[i], eps);
                z[i] = 0.5 + 0.5 * Erf(top / bottom);
            }
            return z;
        }

        private static double Erf(double x)
        {
            double a1 = 0.254829592;
            double a2 = -0.284496736;
            double a3 = 1.421413741;
            double a4 = -1.453152027;
            double a5 = 1.061405429;
            double p = 0.3275911;
            int sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);
            double t = 1.0 / (1.0 + p * x);
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
            return sign * y;
        }

        private static double[] NormalLpdf(double x, double[] weights, double[] mus, double[] sigmas, double pAccept)
        {
            double[] ret = new double[weights.Length];
            for (int i = 0; i < ret.Length; i++) {
                double sigma = Math.Max(sigmas[i], eps);
                double dist = x - mus[i];
                double mahal = ArrayMath.Square(dist / sigma);
                double z = Math.Sqrt(2 * Math.PI) * sigmas[i];
                double coef = weights[i] / z / pAccept;
                ret[i] = -0.5 * mahal + Math.Log(coef);
            }
            return ret;
        }

        private static double[] LognormalLpdf(double x, double[] weights, double[] mus, double[] sigmas)
        {
            double[] ret = new double[weights.Length];
            for (int i = 0; i < ret.Length; i++) {
                double sigma = Math.Max(sigmas[i], eps);
                double z = sigma * x * Math.Sqrt(2 * Math.PI);
                double e = 0.5 * ArrayMath.Square((Math.Log(x) - mus[i]) / sigma);
                ret[i] = -e - Math.Log(z) + Math.Log(weights[i]);
            }
            return ret;
        }

        private static double LogSum(double[] x)
        {
            double max = x.Max();
            double s = 0;
            for (int i = 0; i < x.Length; i++) {
                s += Math.Exp(x[i] - max);
            }
            return Math.Log(s) + max;
        }

        private static double[] Bincount(double[] x, double[] weights, int minlength)
        {
            double[] ret = new double[minlength];
            for (int i = 0; i < x.Length; i++) {
                ret[(int)x[i]] += weights[i];
            }
            return ret;
        }
    }

}
