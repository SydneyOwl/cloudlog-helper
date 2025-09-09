using System;
using System.Collections.Generic;
using System.Linq;
using CloudlogHelper.Models;

namespace CloudlogHelper.Utils;

public class QSOPointUtil
{
    public static List<PolarQSOPoint> GenerateFakeFT8Data(int count)
    {
        var random = new Random();
        var data = new List<PolarQSOPoint>();

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

            data.Add(new PolarQSOPoint
            {
                Azimuth = azimuth,
                Distance = distance
            });
        }

        return data;
    }

    public static double[] CalculateDensitiesKNN(PolarQSOPoint[] points,
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

    public static double CalculateRobustMaxDistance(PolarQSOPoint[] points, double percentile = 0.95)
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

        return Math.Max(robustMax, 500.0); // 至少500km
    }
}