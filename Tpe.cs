using System;
using System.Collections.Generic;
using System.Linq;

namespace Nni
{
    class TpeParameters : Dictionary<string, double> { }

    class Result
    {
        public int id;
        public double loss;
        public TpeParameters param;

        public Result(int id, double loss, TpeParameters param)
        {
            this.id = id;
            this.loss = loss;
            this.param = param;
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

        public static RandomNumberGenerator rng = new RandomNumberGenerator();

        private SearchSpace space;
        private bool minimize;
        private Dictionary<int, TpeParameters> parameters = new Dictionary<int, TpeParameters>();
        private HashSet<int> running = new HashSet<int>();
        private List<Result> history = new List<Result>();
        private double lie = Double.PositiveInfinity;

        public TpeTuner(SearchSpace searchSpace, bool minimizeMode = true)
        {
            this.space = searchSpace;
            this.minimize = minimizeMode;
        }

        public Parameters GenerateParameters(int parameterId)
        {
            Parameters ret;
            TpeParameters param;
            if (parameters.Count > nStartupJobs && running.Count > 0) {
                var fakeHistory = new List<Result>(history);
                foreach (int id in running) {
                    fakeHistory.Add(new Result(fakeHistory.Count, lie, parameters[id]));
                }
                (ret, param) = Suggest(space, fakeHistory);
            } else {
                (ret, param) = Suggest(space, history);
            }
            parameters[parameterId] = param;
            running.Add(parameterId);

            //Console.WriteLine($"----- {parameterId} -----");
            //foreach (var (key, val) in param) {
            //    Console.WriteLine($"{key}: {val}");
            //}

            return ret;
        }

        public void ReceiveTrialResult(int parameterId, double loss)
        {
            if (!minimize) {
                loss = -loss;
            }
            running.Remove(parameterId);
            lie = Math.Min(lie, loss);
            history.Add(new Result(parameterId, loss, parameters[parameterId]));
        }

        private static (Parameters, TpeParameters) Suggest(SearchSpace space, List<Result> history)
        {
            Parameters formattedParam = new Parameters();
            TpeParameters param = new TpeParameters();

            int pipelineIndex = SuggestCategorical(history, "__pipeline__", space.pipelines.Count);
            var chosenPipeline = space.pipelines[pipelineIndex];

            foreach (AlgorithmSpace algo in space.algorithms.Values) {
                if (chosenPipeline.Contains(algo.name)) {
                    var formattedAlgo = new AlgorithmParameters();
                    formattedParam[algo.name] = formattedAlgo;

                    foreach (ParameterRange range in algo) {
                        if (range.isCategorical) {
                            int index = SuggestCategorical(history, range.tag, range.size);
                            param[range.tag] = index;
                            formattedAlgo[range.name] = range.categoricalValues[index];
                        } else {
                            double x = SuggestNumerical(history, range.tag, range.low, range.high, range.isLogDistributed, range.isInteger);
                            param[range.tag] = x;
                            formattedAlgo[range.name] = range.isInteger ? ((int)x).ToString() : x.ToString();
                        }
                    }
                }
            }

            return (formattedParam, param);
        }

        private static int SuggestCategorical(List<Result> history, string tag, int size)
        {
            if (history.Count < nStartupJobs) {
                return rng.Integer(size);
            }

            var (obsBelow, obsAbove) = ApSplitTrials(history, tag);

            double[] weights = LinearForgettingWeights(obsBelow.Length);
            double[] counts = Bincount(obsBelow, weights, size);
            double[] p = ArrayMath.DivSum(ArrayMath.Add(counts, priorWeight));
            int[] sample = rng.Categorical(p, nEiCandidates);
            double[] belowLLik = ArrayMath.Log(ArrayMath.Index(p, sample));

            weights = LinearForgettingWeights(obsAbove.Length);
            counts = Bincount(obsAbove, weights, size);
            p = ArrayMath.DivSum(ArrayMath.Add(counts, priorWeight));
            double[] aboveLLik = ArrayMath.Log(ArrayMath.Index(p, sample));

            return FindBest(sample, belowLLik, aboveLLik);
        }

        private static double SuggestNumerical(List<Result> history, string tag, double low, double high, bool log, bool integer)
        {
            if (history.Count < nStartupJobs) {
                double x = rng.Uniform(low, high);
                if (log) { x = Math.Exp(x); }
                if (integer) { x = Math.Round(x); }
                return x;
            }

            var (obsBelow, obsAbove) = ApSplitTrials(history, tag);

            if (log) {
                obsBelow = ArrayMath.Log(obsBelow);
                obsAbove = ArrayMath.Log(obsAbove);
            }

            double priorMu = 0.5 * (high + low);
            double priorSigma = high - low;

            var (weights, mus, sigmas) = AdaptiveParzenNormal(obsBelow, priorMu, priorSigma);
            double[] samples = Gmm1(weights, mus, sigmas, low, high, log, integer);
            double[] belowLLik = Gmm1Lpdf(samples, weights, mus, sigmas, low, high, log, integer);

            (weights, mus, sigmas) = AdaptiveParzenNormal(obsAbove, priorMu, priorSigma);
            double[] aboveLLik = Gmm1Lpdf(samples, weights, mus, sigmas, low, high, log, integer);

            return FindBest(samples, belowLLik, aboveLLik);
        }

        private static (double[], double[]) ApSplitTrials(List<Result> history, string tag)
        {
            int nBelow = Math.Min((int)Math.Ceiling(olossGamma * Math.Sqrt(history.Count)), lf);
            var sorted = history.OrderBy(result => result.loss);
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

        private static double[] LinearForgettingWeights(int n)
        {
            double[] weights = Enumerable.Repeat(1.0, n).ToArray();
            double rampStart = 1.0 / n;
            int rampLength= n - lf;
            if (rampLength == 1) {
                weights[0] = rampStart;
            } else {
                for (int i = 0; i < rampLength; i++) {
                    weights[i] = rampStart + (1.0 - rampStart) / (rampLength - 1) * i;
                }
            }
            return weights;
        }

        private static double[] Gmm1(double[] weights, double[] mus, double[] sigmas, double low, double high, bool log, bool integer)
        {
            double[] samples = new double[nEiCandidates];
            for (int i = 0; i < nEiCandidates; i++) {
                while (true) {
                    int active = rng.Categorical(weights);
                    double draw = rng.Normal(mus[active], sigmas[active]);
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
            double pAccept = ArrayMath.Mul(weights, ArrayMath.Sub(NormalCdf(high, mus, sigmas), NormalCdf(low, mus, sigmas))).Sum();

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
