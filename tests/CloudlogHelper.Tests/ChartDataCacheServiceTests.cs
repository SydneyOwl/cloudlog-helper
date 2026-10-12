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
        var snapshot = service.GetStationChartDataSnapshot("10m", "FT8");
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

        var snapshot = service.GetStationChartDataSnapshot("10m", "FT8");

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

        var accumulated = service.GetStationChartDataSnapshot("10m", "FT8", filterDupeByCallsign: false);
        var deduped = service.GetStationChartDataSnapshot("10m", "FT8", filterDupeByCallsign: true);

        Assert.Equal(2d, accumulated.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(1d, accumulated.StationCountByDxcc["K"] ?? 0d);
        Assert.Equal(3d, accumulated.DistanceHistogram.Counts.Sum());
        Assert.Equal(3d, accumulated.GridStationCount.Cast<double>().Sum());

        Assert.Equal(1d, deduped.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(1d, deduped.StationCountByDxcc["K"] ?? 0d);
        Assert.Equal(2d, deduped.DistanceHistogram.Counts.Sum());
        Assert.Equal(2d, deduped.GridStationCount.Cast<double>().Sum());
    }

    [Fact]
    public void Snapshot_WhenModeSelected_OnlyCountsThatMode()
    {
        var service = new ChartDataCacheService();

        service.Add(_createPoint("BG7AA", "10m", "BY", 1000, 30, 30, 114, true));
        service.Add(_createPoint("K1ABC", "10m", "K", 8500, 75, 40, -74, true));
        service.Add(_createPoint("JA1ABC", "10m", "JA", 2000, 120, 35, 139, true, "FT4"));

        var ft8 = service.GetStationChartDataSnapshot("10m", "FT8");
        var ft4 = service.GetStationChartDataSnapshot("10m", "FT4");
        var merged = service.GetStationChartDataSnapshot("10m", null);
        var unknown = service.GetStationChartDataSnapshot("10m", "Q65");

        Assert.Equal(1d, ft8.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(1d, ft8.StationCountByDxcc["K"] ?? 0d);
        Assert.False(ft8.StationCountByDxcc.ContainsKey("JA"));
        Assert.Equal(2d, ft8.DistanceHistogram.Counts.Sum());

        Assert.Equal(1d, ft4.StationCountByDxcc["JA"] ?? 0d);
        Assert.Single(ft4.StationCountByDxcc);
        Assert.Equal(1d, ft4.DistanceHistogram.Counts.Sum());

        // empty mode falls back to merging every mode bucket of the band
        Assert.Equal(3d, merged.StationCountByDxcc.Values.Sum(x => x ?? 0d));
        Assert.Equal(3d, merged.DistanceHistogram.Counts.Sum());

        Assert.Empty(unknown.StationCountByDxcc);
    }

    [Fact]
    public void Snapshot_WhenModeSelected_MatchesCaseInsensitively()
    {
        var service = new ChartDataCacheService();
        service.Add(_createPoint("BG7AA", "10m", "BY", 1000, 30, 30, 114, true));

        var lowercase = service.GetStationChartDataSnapshot("10m", "ft8");

        Assert.Equal(1d, lowercase.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(1d, lowercase.DistanceHistogram.Counts.Sum());
    }

    [Fact]
    public void Snapshot_WhenModeChangesOnSameBand_DedupKeepsLatestPointPerMode()
    {
        var service = new ChartDataCacheService();

        // the same callsign shows up in two modes on the same band
        service.Add(_createPoint("BG7AA", "10m", "BY", 1000, 30, 30, 114, true));
        service.Add(_createPoint("BG7AA", "10m", "BY", 1200, 60, 31, 115, true, "FT4"));

        var ft8 = service.GetStationChartDataSnapshot("10m", "FT8", filterDupeByCallsign: true);
        var ft4 = service.GetStationChartDataSnapshot("10m", "FT4", filterDupeByCallsign: true);
        var merged = service.GetStationChartDataSnapshot("10m", null, filterDupeByCallsign: true);

        Assert.Equal(1d, ft8.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(1d, ft8.DistanceHistogram.Counts.Sum());

        Assert.Equal(1d, ft4.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(1d, ft4.DistanceHistogram.Counts.Sum());

        // the merged view sees one latest point per mode bucket
        Assert.Equal(2d, merged.StationCountByDxcc["BY"] ?? 0d);
        Assert.Equal(2d, merged.DistanceHistogram.Counts.Sum());
    }

    private static ChartQSOPoint _createPoint(
        string callsign,
        string band,
        string dxcc,
        double distance,
        double azimuth,
        double latitude,
        double longitude,
        bool isAccurate,
        string mode = "FT8")
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
            Mode = mode,
            Snr = -10
        };
    }
}
