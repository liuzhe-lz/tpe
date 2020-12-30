using System;
using System.Linq;

namespace Nni {
class ArrayMath{

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

public static int SearchSorted(double[] array, double value)
{
    int index = Array.BinarySearch(array, value);
    return index >= 0 ? index : ~index;
}

public static int[] ArgSort(double[] array)
{
    return Enumerable.Range(0, array.Length).OrderBy(index => array[index]).ToArray();
}

public static double[] MaximumInPlace(double[] array, double value)
{
    for (int i = 0; i < array.Length; i++) {
        array[i] = Math.Max(array[i], value);
    }
    return array;
}

public static double[] LogInPlace(double[] array)
{
    for (int i = 0; i < array.Length; i++) {
        array[i] = Math.Log(array[i]);
    }
    return array;
}

public static double[] Index(double[] array, int[] indices)
{
    return indices.Select(index => array[index]).ToArray();
}

public static double[] Insert(double[] array, int index, double value)
{
    double[] ret = new double[array.Length + 1];
    Array.Copy(array, 0, ret, 0, index);
    ret[index] = value;
    Array.Copy(array, index, ret, index + 1, array.Length - index);
    return ret;
}

public static double Sum(double[] array)
{
    return Enumerable.Sum(array);
}

public static double[] Add(double[] xArray, double y)
{
    return xArray.Select(x => x + y).ToArray();
}

public static double[] Add(double[] xArray, double[] yArray)
{
    return Enumerable.Zip(xArray, yArray, (x, y) => x + y).ToArray();
}

public static double[] Sub(double[] xArray, double[] yArray)
{
    return Enumerable.Zip(xArray, yArray, (x, y) => x - y).ToArray();
}

public static double[] Mul(double[] xArray, double[] yArray)
{
    return Enumerable.Zip(xArray, yArray, (x, y) => x * y).ToArray();
}

public static double[] Div(double[] xArray, double y)
{
    return xArray.Select(x => x / y).ToArray();
}

public static double[] Max(double[] xArray, double y)
{
    return xArray.Select(x => Math.Max(x, y)).ToArray();
}

public static double[] Clip(double[] xArray, double min, double max)
{
    return xArray.Select(x => Math.Min(Math.Max(x, min), max)).ToArray();
}

public static double[] Log(double[] xArray)
{
    return xArray.Select(x => Math.Log(x)).ToArray();
}

public static double[] DivSum(double[] xArray)
{
    return Div(xArray, xArray.Sum());
}

public static double Square(double x)
{
    return x * x;
}

}
}
