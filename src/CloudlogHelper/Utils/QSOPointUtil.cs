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
                Mode = new[]{"FT8","FT4"}[new Random().Next(0,2)],
                Snr = 0,
                Band = new[]{"40m","20m","10m"}[new Random().Next(0,3)],
                Client = new[]{"WSJT-X","JTDX","PPSK"}[new Random().Next(0,3)],
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
        {
            for (var x = 0; x < width; x++)
            {
                var normalized = (data[y, x] - oldMin) / (oldMax - oldMin);
                var scaled = newMin + normalized * (newMax - newMin);

                // Clamp the value to [newMin, newMax]
                result[y, x] = Math.Max(newMin, Math.Min(newMax, scaled));
            }
        }

        return result;
    }

    // 范围
    public static double[,] ApplyValueCompression(double[,] data)
    {
        var maxVal = data.Cast<double>().Max();
        var logData = new double[data.GetLength(0), data.GetLength(1)];
        for (var i = 0; i < data.GetLength(0); i++)
        {
            for (var j = 0; j < data.GetLength(1); j++) 
            {
                var value = data[i, j];
                if (value == 0) continue;
                // if (value is > 0 and < 10) value = 10;
                logData[i, j] = Math.Log10(value + 1);
            }
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
        {
            for (var x = 0; x < width; x++)
            {
                double sum = 0;
                double weightSum = 0;
            
                for (var ky = -kernelSize/2; ky <= kernelSize/2; ky++)
                {
                    for (var kx = -kernelSize/2; kx <= kernelSize/2; kx++)
                    {
                        var nx = x + kx;
                        var ny = y + ky;
                    
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            var weight = kernel[ky + kernelSize/2, kx + kernelSize/2];
                            sum += data[ny, nx] * weight;
                            weightSum += weight;
                        }
                    }
                }
            
                result[y, x] = sum / weightSum;
            }
        }
    
        return result;
    }

    private static double[,] CreateGaussianKernel(int size, double sigma)
    {
        var kernel = new double[size, size];
        double sum = 0;
        var center = size / 2;
    
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                double dx = x - center;
                double dy = y - center;
                kernel[y, x] = Math.Exp(-(dx*dx + dy*dy) / (2 * sigma * sigma));
                sum += kernel[y, x];
            }
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
        {
            for (var x = 0; x < newWidth; x++)
            {
                var origY = (double)y / factor;
                var origX = (double)x / factor;
            
                result[y, x] = BilinearInterpolate(originalData, origX, origY);
            }
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
    /// 用KNN算法评估信号点之间的接近程度。支持使用距离或角度评估。
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
        var densities = new double[points.Length];
        const double epsilon = 0.0001;

        for (var i = 0; i < points.Length; i++)
        {
            var currentPoint = points[i];
            var kNearestDistances = new SortedSet<double>();

            for (var j = 0; j < points.Length; j++)
            {
                if (i == j) continue;

                var otherPoint = points[j];

                var normDistanceDiff = Math.Abs(currentPoint.Distance - otherPoint.Distance) / maxDistance;
                var angleDiff = Math.Abs(currentPoint.Azimuth - otherPoint.Azimuth);
                angleDiff = Math.Min(angleDiff, 360 - angleDiff);
                var normAngleDiff = angleDiff / 180.0;

                var combinedDistance = Math.Sqrt(
                    distanceWeight * normDistanceDiff * normDistanceDiff +
                    angleWeight * normAngleDiff * normAngleDiff
                );
                if (kNearestDistances.Count < k)
                {
                    kNearestDistances.Add(combinedDistance);
                }
                else if (combinedDistance < kNearestDistances.Max)
                {
                    kNearestDistances.Remove(kNearestDistances.Max);
                    kNearestDistances.Add(combinedDistance);
                }
            }

            densities[i] = 1.0 / (kNearestDistances.Max + epsilon);
        }

        return densities;
    }

    /// <summary>
    /// 保守地计算数据集中前n%的平均值作为最大值
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