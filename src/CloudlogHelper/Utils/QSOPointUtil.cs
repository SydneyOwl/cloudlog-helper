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