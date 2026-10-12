using System;
using System.Collections.Generic;
using System.Linq;
using CloudlogHelper.Models;

namespace CloudlogHelper.Utils;

public class QSOPointUtil
{
    public static List<ChartQSOPoint> GenerateFakeFT8Data(int count)
    {
        var random = new Random();
        var data = new List<ChartQSOPoint>();

        var hotSpots = new[] { 45, 120, 300, 30, 200 };

        for (var i = 0; i < count; i++)
        {
            double azimuth;
            double distance;

            if (random.NextDouble() < 0.6)
            {
                var hotspotIndex = random.Next(hotSpots.Length);
                azimuth = hotSpots[hotspotIndex] + (random.NextDouble() - 0.5) * 20;
            }
            else
            {
                azimuth = random.NextDouble() * 360;
            }

            var distanceType = random.NextDouble();
            if (distanceType < 0.4)
                distance = random.NextDouble() * 2000;
            else if (distanceType < 0.7)
                distance = 2000 + random.NextDouble() * 4000;
            else if (distanceType < 0.9)
                distance = 6000 + random.NextDouble() * 4000;
            else
                distance = 10000 + random.NextDouble() * 5000;

            data.Add(new ChartQSOPoint
            {
                DxCallsign = null,
                Azimuth = azimuth,
                Distance = distance,
                Mode = new[] { "FT8", "FT4" }[new Random().Next(0, 2)],
                Snr = 0,
                Band = new[] { "40m", "20m", "10m" }[new Random().Next(0, 3)],
                Client = new[] { "WSJT-X", "JTDX", "PPSK" }[new Random().Next(0, 3)]
            });
        }

        return data;
    }

    public static double[,] NormalizeData(double[,] data, double newMin, double newMax)
    {
        var height = data.GetLength(0);
        var width = data.GetLength(1);
        var result = new double[height, width];

        var oldMin = data.Cast<double>().Min();
        var oldMax = data.Cast<double>().Max();

        if (Math.Abs(oldMax - oldMin) < 0.0001)
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                result[y, x] = newMin;
            return result;
        }

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var normalized = (data[y, x] - oldMin) / (oldMax - oldMin);
            var scaled = newMin + normalized * (newMax - newMin);

            // Clamp the value to [newMin, newMax]
            result[y, x] = Math.Max(newMin, Math.Min(newMax, scaled));
        }

        return result;
    }

    // 范围
    public static double[,] ApplyValueCompression(double[,] data)
    {
        var logData = new double[data.GetLength(0), data.GetLength(1)];
        for (var i = 0; i < data.GetLength(0); i++)
        for (var j = 0; j < data.GetLength(1); j++)
        {
            var value = data[i, j];
            if (value == 0) continue;
            // if (value is > 0 and < 10) value = 10;
            logData[i, j] = Math.Log10(value + 1);
        }

        return logData;
    }

    // 应用高斯卷积
    public static double[,] ApplyGaussianBlur(double[,] data, double sigma = 1.0)
    {
        var height = data.GetLength(0);
        var width = data.GetLength(1);
        var result = new double[height, width];

        var kernelSize = (int)(sigma * 3) * 2 + 1; // 核大小
        var kernel = CreateGaussianKernel(kernelSize, sigma);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            double sum = 0;
            double weightSum = 0;

            for (var ky = -kernelSize / 2; ky <= kernelSize / 2; ky++)
            for (var kx = -kernelSize / 2; kx <= kernelSize / 2; kx++)
            {
                var nx = x + kx;
                var ny = y + ky;

                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    var weight = kernel[ky + kernelSize / 2, kx + kernelSize / 2];
                    sum += data[ny, nx] * weight;
                    weightSum += weight;
                }
            }

            result[y, x] = sum / weightSum;
        }

        return result;
    }

    private static double[,] CreateGaussianKernel(int size, double sigma)
    {
        var kernel = new double[size, size];
        double sum = 0;
        var center = size / 2;

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            double dx = x - center;
            double dy = y - center;
            kernel[y, x] = Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma));
            sum += kernel[y, x];
        }

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            kernel[y, x] /= sum;

        return kernel;
    }

    // 数据插值
    public static double[,] InterpolateData(double[,] originalData, int factor)
    {
        var origHeight = originalData.GetLength(0);
        var origWidth = originalData.GetLength(1);
        var newHeight = origHeight * factor;
        var newWidth = origWidth * factor;

        var result = new double[newHeight, newWidth];

        for (var y = 0; y < newHeight; y++)
        for (var x = 0; x < newWidth; x++)
        {
            var origY = (double)y / factor;
            var origX = (double)x / factor;

            result[y, x] = BilinearInterpolate(originalData, origX, origY);
        }

        return result;
    }

    // 双线性插值
    public static double BilinearInterpolate(double[,] data, double x, double y)
    {
        var x1 = (int)Math.Floor(x);
        var y1 = (int)Math.Floor(y);
        var x2 = Math.Min(x1 + 1, data.GetLength(1) - 1);
        var y2 = Math.Min(y1 + 1, data.GetLength(0) - 1);

        var dx = x - x1;
        var dy = y - y1;

        var v11 = data[y1, x1];
        var v12 = data[y1, x2];
        var v21 = data[y2, x1];
        var v22 = data[y2, x2];

        return v11 * (1 - dx) * (1 - dy) +
               v12 * dx * (1 - dy) +
               v21 * (1 - dx) * dy +
               v22 * dx * dy;
    }

    /// <summary>
    ///     用KNN算法评估信号点之间的接近程度。支持使用距离或角度评估。
    /// </summary>
    /// <param name="points"></param>
    /// <param name="maxDistance"></param>
    /// <param name="k"></param>
    /// <param name="distanceWeight"></param>
    /// <param name="angleWeight"></param>
    /// <returns></returns>
    public static double[] CalculateDensitiesKNN(ChartQSOPoint[] points,
        double maxDistance,
        int k = 5,
        double distanceWeight = 1.0,
        double angleWeight = 1.0)
    {
        var n = points.Length;
        var densities = new double[n];
        if (n <= 1) return densities;

        k = Math.Clamp(k, 1, n - 1);
        const double epsilon = 0.0001;

        // sqrt is monotonic, so the pair loop compares squared distances and only
        // the surviving k-th value is square-rooted once per point; the divisions
        // and weights are folded into two multipliers.
        var w1 = distanceWeight / (maxDistance * maxDistance);
        var w2 = angleWeight / (180.0 * 180.0);

        // Row i of `best` keeps the k smallest squared distances to point i in
        // ascending order, duplicates included (equal distances are distinct
        // neighbours); counts[i] is the number of valid entries in row i.
        var best = new double[n * k];
        var counts = new int[n];

        // The metric is symmetric, so each pair is evaluated once and fed to the
        // top-k rows of both of its endpoints.
        for (var i = 0; i < n; i++)
        {
            var di = points[i].Distance;
            var ai = points[i].Azimuth;

            for (var j = i + 1; j < n; j++)
            {
                var dd = di - points[j].Distance;
                var da = Math.Abs(ai - points[j].Azimuth);
                if (da > 180) da = 360 - da;

                var distSq = w1 * dd * dd + w2 * da * da;

                _tryInsert(best, counts, k, i, distSq);
                _tryInsert(best, counts, k, j, distSq);
            }
        }

        // A row always holds at least one entry: with n >= 2 every point
        // participates in at least one pair. When the point has fewer than k
        // neighbours the largest valid entry stands in for the k-th neighbour.
        for (var i = 0; i < n; i++)
            densities[i] = 1.0 / (Math.Sqrt(best[i * k + counts[i] - 1]) + epsilon);

        return densities;
    }

    private static void _tryInsert(double[] best, int[] counts, int k, int idx, double distSq)
    {
        var row = idx * k;
        var count = counts[idx];

        if (count < k)
        {
            var pos = count;
            while (pos > 0 && best[row + pos - 1] > distSq)
            {
                best[row + pos] = best[row + pos - 1];
                pos--;
            }

            best[row + pos] = distSq;
            counts[idx] = count + 1;
            return;
        }

        if (distSq >= best[row + k - 1]) return;

        var p = k - 1;
        while (p > 0 && best[row + p - 1] > distSq)
        {
            best[row + p] = best[row + p - 1];
            p--;
        }

        best[row + p] = distSq;
    }

    /// <summary>
    ///     保守地计算数据集中前n%的平均值作为最大值
    /// </summary>
    /// <param name="points"></param>
    /// <param name="percentile"></param>
    /// <returns></returns>
    public static double CalculateRobustMaxDistance(ChartQSOPoint[] points, double percentile = 0.95)
    {
        if (points.Length == 0)
            return 3000.0;

        var sortedDistances = points.Select(p => p.Distance)
            .OrderBy(d => d)
            .ToArray();

        if (sortedDistances.Length < 50)
            return sortedDistances.Last();

        var startIndex = (int)(sortedDistances.Length * percentile);
        var count = sortedDistances.Length - startIndex;

        count = Math.Max(1, Math.Min(count, sortedDistances.Length));

        var robustMax = sortedDistances.Skip(startIndex).Take(count).Average();

        return Math.Max(robustMax, 500.0);
    }
}