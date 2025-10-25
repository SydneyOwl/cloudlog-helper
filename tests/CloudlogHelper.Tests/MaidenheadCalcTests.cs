using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

public class MaidenheadCalcTests
{
    [Theory]
    [InlineData("OL94", true)]
    [InlineData("PM01", true)]
    [InlineData("PMA1", false)]
    [InlineData("3MC2", false)]
    [InlineData("RR73", false)]
    [InlineData("RRR", false)]
    public void TestVerifyMaidenhead_ReturnsCorrectResult(string grid, bool expected)
    {
        Assert.Equal(expected, MaidenheadGridUtil.CheckMaidenhead(grid));
    }

    [Theory]
    [InlineData("OL94", 118.91, 24.45)]
    [InlineData("OM89", 116.91, 39.45)]
    [InlineData("IN82", -3.08, 42.46)]
    [InlineData("DM97", -101.36, 37.08)]
    public void TestMaidenheadToLatLon_ReturnsCorrectResult(string grid, double longitude, double latitude)
    {
        Assert.Equal(latitude, MaidenheadGridUtil.GridToLatLng(grid)!.Latitude, 0.5f);
        Assert.Equal(longitude, MaidenheadGridUtil.GridToLatLng(grid)!.Longitude, 0.5f);
    }

    [Theory]
    [InlineData("OL94", "OM89", 1655.39)]
    [InlineData("OL94", "IN82", 10436.66)]
    [InlineData("OL94", "DM97", 12005.60)]
    public void TestDistanceBetweenGrids_ReturnsCorrectResult(string deGrid, string dxGrid, double expectDistance)
    {
        Assert.Equal(expectDistance, MaidenheadGridUtil.GetDist(deGrid, dxGrid), 70f);
    }

    [Theory]
    [InlineData("OL94", "OM89", 354)]
    [InlineData("OL94", "IN82", 321)]
    [InlineData("OL94", "DM97", 32)]
    [InlineData("OL94", "OL94", 0)]
    [InlineData("OL94", "PL02", 137)]
    [InlineData("OL94", "FE40", 165)]
    [InlineData("OL94", "HQ40", 350)]
    public void TestBearingBetweenGrids_ReturnsCorrectResult(string deGrid, string dxGrid, double expectBearing)
    {
        Assert.Equal(expectBearing, MaidenheadGridUtil.CalculateBearing(deGrid, dxGrid), 5f);
    }
}