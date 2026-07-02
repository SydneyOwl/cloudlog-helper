using System.Linq;
using CloudlogHelper.Models;
using CloudlogHelper.Services;

namespace CloudlogHelper.Tests;

public class ChartDataCacheServiceTests
{
    [Fact]
    public void Add_PublishesItemAddedNotification()
    {
        using var service = new ChartDataCacheService();
        var received = new List<ChartQSOPoint>();
        using var subscription = service.GetItemAddedObservable().Subscribe(received.Add);
        var point = _createPoint(
            callsign: "BG7AA",
            band: "10m",
            dxcc: "BY",
            distance: 1000,
            azimuth: 30,
            latitude: 30,
            longitude: 114,
            isAccurate: true);

        service.Add(point);

        var receivedPoint = Assert.Single(received);
        Assert.Equal(point.DxCallsign, receivedPoint.DxCallsign);
        Assert.Equal(point.Band, receivedPoint.Band);
        Assert.Equal(point.DXCC, receivedPoint.DXCC);
    }

    [Fact]
    public void TakeLatestN_ReturnsNewestFirst_AndAppliesFilter()
    {
        using var service = new ChartDataCacheService();
        var first = _createPoint("BG7AA", "10m", "BY", 1000, 30, 30, 114, true);
        var second = _createPoint("K1ABC", "20m", "K", 8500, 75, 40, -74, true);
        var third = _createPoint("JA1ABC", "10m", "JA", 2000, 120, 35, 139, true);

        service.Add(first);
        service.Add(second);
        service.Add(third);

        var latest10m = service.TakeLatestN(2, filterCondition: x => x.Band == "10m").ToList();

        Assert.Equal(new[] { third, first }, latest10m);
    }

    [Fact]
    public void Clear_RemovesPolarAndAccumulatedData()
    {
        using var service = new ChartDataCacheService();
        service.Add(_createPoint("BG7AA", "10m", "BY", 1000, 30, 30, 114, true));

        service.Clear();

        Assert.Empty(service.TakeLatestN(10));
        var snapshot = service.GetStationChartDataSnapshotByBand("10m");
        Assert.Empty(snapshot.StationCountByDxcc);
        Assert.Equal(0d, snapshot.DistanceHistogram.Counts.Sum());
    }

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
