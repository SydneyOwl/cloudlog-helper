using CloudlogHelper.Models;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

public class QSOPointUtilTests
{
    [Fact]
    public void NormalizeData_ScalesValuesIntoRequestedRange()
    {
        var data = new[,]
        {
            { 0d, 5d },
            { 10d, 20d }
        };

        var normalized = QSOPointUtil.NormalizeData(data, -1, 1);

        Assert.Equal(-1d, normalized[0, 0], precision: 8);
        Assert.Equal(1d, normalized[1, 1], precision: 8);
        Assert.True(normalized[0, 1] is > -1d and < 1d);
    }

    [Fact]
    public void NormalizeData_WhenAllValuesEqual_FillsWithNewMinimum()
    {
        var data = new[,]
        {
            { 7d, 7d },
            { 7d, 7d }
        };

        var normalized = QSOPointUtil.NormalizeData(data, 3, 9);

        Assert.All(normalized.Cast<double>(), value => Assert.Equal(3d, value));
    }

    [Fact]
    public void BilinearInterpolate_ReturnsWeightedMiddleValue()
    {
        var data = new[,]
        {
            { 0d, 10d },
            { 20d, 30d }
        };

        var value = QSOPointUtil.BilinearInterpolate(data, 0.5, 0.5);

        Assert.Equal(15d, value, precision: 8);
    }

    [Fact]
    public void CalculateRobustMaxDistance_UsesLargestDistanceForSmallDatasets()
    {
        var points = new[]
        {
            new ChartQSOPoint { Distance = 100 },
            new ChartQSOPoint { Distance = 800 },
            new ChartQSOPoint { Distance = 400 }
        };

        Assert.Equal(800d, QSOPointUtil.CalculateRobustMaxDistance(points));
    }

    [Fact]
    public void CalculateDensitiesKNN_TreatsAnglesAcrossZeroAsClose()
    {
        var points = new[]
        {
            new ChartQSOPoint { Distance = 1000, Azimuth = 359 },
            new ChartQSOPoint { Distance = 1000, Azimuth = 1 },
            new ChartQSOPoint { Distance = 1000, Azimuth = 180 }
        };

        var densities = QSOPointUtil.CalculateDensitiesKNN(points, maxDistance: 20000, k: 1);

        Assert.True(densities[0] > densities[2]);
        Assert.True(densities[1] > densities[2]);
    }
}
