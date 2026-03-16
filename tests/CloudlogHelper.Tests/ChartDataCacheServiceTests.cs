using System.Linq;
using CloudlogHelper.Models;
using CloudlogHelper.Services;

namespace CloudlogHelper.Tests;

public class ChartDataCacheServiceTests
{
    [Fact]
    public void Add_WhenPointIsNotAccurate_ShouldSkipSpatialAccumulators()
    {
        var service = new ChartDataCacheService();

        service.Add(_createPoint(
            callsign: "BG7AA",
            band: "10m",
            dxcc: "BY",
            distance: 1000,
            azimuth: 30,
            latitude: 30,
            longitude: 114,
            isAccurate: false));

        var snapshot = service.GetStationChartDataSnapshotByBand("10m");

        Assert.Equal(1d, snapshot.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(0d, snapshot.DistanceHistogram.Counts.Sum());
        Assert.Equal(0d, snapshot.BearingHistogram.Counts.Sum());
        Assert.Equal(0d, snapshot.GridStationCount.Cast<double>().Sum());
    }

    [Fact]
    public void Snapshot_WhenFilterDupeCallsignEnabled_ShouldKeepLatestPointPerCallsign()
    {
        var service = new ChartDataCacheService();

        service.Add(_createPoint(
            callsign: "BG7AA",
            band: "10m",
            dxcc: "BY",
            distance: 1000,
            azimuth: 30,
            latitude: 30,
            longitude: 114,
            isAccurate: true));

        service.Add(_createPoint(
            callsign: "BG7AA",
            band: "10m",
            dxcc: "BY",
            distance: 1200,
            azimuth: 60,
            latitude: 31,
            longitude: 115,
            isAccurate: true));

        service.Add(_createPoint(
            callsign: "K1ABC",
            band: "10m",
            dxcc: "K",
            distance: 8500,
            azimuth: 75,
            latitude: 40,
            longitude: -74,
            isAccurate: true));

        var accumulated = service.GetStationChartDataSnapshotByBand("10m", filterDupeByCallsign: false);
        var deduped = service.GetStationChartDataSnapshotByBand("10m", filterDupeByCallsign: true);

        Assert.Equal(2d, accumulated.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(1d, accumulated.StationCountByDxcc["K"] ?? 0d);
        Assert.Equal(3d, accumulated.DistanceHistogram.Counts.Sum());
        Assert.Equal(3d, accumulated.GridStationCount.Cast<double>().Sum());

        Assert.Equal(1d, deduped.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(1d, deduped.StationCountByDxcc["K"] ?? 0d);
        Assert.Equal(2d, deduped.DistanceHistogram.Counts.Sum());
        Assert.Equal(2d, deduped.GridStationCount.Cast<double>().Sum());
    }

    private static ChartQSOPoint _createPoint(
        string callsign,
        string band,
        string dxcc,
        double distance,
        double azimuth,
        double latitude,
        double longitude,
        bool isAccurate)
    {
        return new ChartQSOPoint
        {
            DxCallsign = callsign,
            Band = band,
            DXCC = dxcc,
            Distance = distance,
            Azimuth = azimuth,
            Latitude = latitude,
            Longitude = longitude,
            IsAccurate = isAccurate,
            Client = "test",
            Mode = "FT8",
            Snr = -10
        };
    }
}
