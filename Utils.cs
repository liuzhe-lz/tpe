using System;
using System.Linq;

namespace Nni {
    class RandomNumberGenerator
    {
        private Random random;

        public RandomNumberGenerator(int seed = 0)
        {
            random = new Random(seed);
        }

        public int Integer(int high)
        {
            return random.Next(high);
        }

        public double Uniform(double low, double high)
        {
            return random.NextDouble() * (high - low) + low;
        }

        public double Normal(double location, double scale)
        {
            double u = 1 - Uniform(0, 1);
            double v = 1 - Uniform(0, 1);
            double std = Math.Sqrt(-2.0 * Math.Log(u)) * Math.Sin(2.0 * Math.PI * v);
            return location + std * scale;
        }

        public int Categorical(double[] possibility)
        {
            double x = Uniform(0, 1);
            for (int i = 0; i < possibility.Length; i++) {
                x -= possibility[i];
                if (x < 0) { return i; }
            }
            return possibility.Length - 1;
        }

        public int[] Categorical(double[] possibility, int size)
        {
            int[] ret = new int[size];
            for (int i = 0; i < ret.Length; i++) {
                ret[i] = Categorical(possibility);
            }
            return ret;
        }
    }

    class ArrayMath{
        /* x + y */
        public static double[] Add(double[] xArray, double y)
        {
            return xArray.Select(x => x + y).ToArray();
        }

        /* np.argmax */
        public static int ArgMax(double[] array)
        {
            int index = 0;
            for (int i = 1; i < array.Length; i++) {
                if (array[i] > array[index]) {
                    index = i;
                }
            }
            return index;
        }

        /* np.argsort */
        public static int[] ArgSort(double[] array)
        {
            return Enumerable.Range(0, array.Length).OrderBy(index => array[index]).ToArray();
        }

        /* np.clip */
        public static double[] Clip(double[] xArray, double min, double max)
        {
            return xArray.Select(x => Math.Min(Math.Max(x, min), max)).ToArray();
        }

        /* x / y */
        public static double[] Div(double[] xArray, double y)
        {
            return xArray.Select(x => x / y).ToArray();
        }

        /* x / np.sum(x) */
        public static double[] DivSum(double[] xArray)
        {
            return Div(xArray, xArray.Sum());
        }

        /* x[y] */
        public static double[] Index(double[] array, int[] indices)
        {
            return indices.Select(index => array[index]).ToArray();
        }

        /* List.Insert */
        public static double[] Insert(double[] array, int index, double item)
        {
            double[] ret = new double[array.Length + 1];
            Array.Copy(array, 0, ret, 0, index);
            ret[index] = item;
            Array.Copy(array, index, ret, index + 1, array.Length - index);
            return ret;
        }

        /* np.log */
        public static double[] Log(double[] xArray)
        {
            return xArray.Select(x => Math.Log(x)).ToArray();
        }

        /* x * y */
        public static double[] Mul(double[] xArray, double[] yArray)
        {
            return Enumerable.Zip(xArray, yArray, (x, y) => x * y).ToArray();
        }

        /* np.searchsorted */
        public static int SearchSorted(double[] array, double item)
        {
            int index = Array.BinarySearch(array, item);
            return index >= 0 ? index : ~index;
        }

        /* x ^ 2 */
        public static double Square(double x)
        {
            return x * x;
        }

        /* x - y */
        public static double[] Sub(double[] xArray, double[] yArray)
        {
            return Enumerable.Zip(xArray, yArray, (x, y) => x - y).ToArray();
        }
    }
}
