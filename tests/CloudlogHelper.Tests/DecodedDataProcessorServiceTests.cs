using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using CloudlogHelper.Database;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Tests;

public class DecodedDataProcessorServiceTests
{
    [Fact]
    public async Task ProcessDecoded_BlankId_KeysTheChartPointBySourceAddress()
    {
        // The chart used to key points by the raw Id, so a blank Id produced Client = "" while
        // the tab was keyed "10.0.0.5" - the filter never matched and that client's polar chart
        // stayed empty. Both must come from GetClientKey, and the band lookup keys the same way.
        using var chartCache = new ChartDataCacheService();
        using var processor = new DecodedDataProcessorService(
            new StubDatabaseService(), new SettingsWithGrid(), chartCache);

        var points = new List<ChartQSOPoint>();
        using var subscription = chartCache.GetItemAddedObservable().Subscribe(points.Add);

        processor.UpdateClientBand(string.Empty, "20m", Source("10.0.0.5"));
        processor.ProcessDecoded(Decode(string.Empty, "CQ BG7AA OL63"), Source("10.0.0.5"));

        await WaitForAsync(() => points.Count > 0);

        var point = Assert.Single(points);
        Assert.Equal("10.0.0.5", point.Client);
        Assert.Equal("20m", point.Band);
    }

    [Fact]
    public async Task ProcessDecoded_NonBlankId_KeepsTheIdAsTheChartKey()
    {
        using var chartCache = new ChartDataCacheService();
        using var processor = new DecodedDataProcessorService(
            new StubDatabaseService(), new SettingsWithGrid(), chartCache);

        var points = new List<ChartQSOPoint>();
        using var subscription = chartCache.GetItemAddedObservable().Subscribe(points.Add);

        processor.UpdateClientBand("WSJT-X - 20m", "20m", Source("10.0.0.5"));
        processor.ProcessDecoded(Decode("WSJT-X - 20m", "CQ BG7AA OL63"), Source("10.0.0.5"));

        await WaitForAsync(() => points.Count > 0);

        var point = Assert.Single(points);
        Assert.Equal("WSJT-X - 20m", point.Client);
        Assert.Equal("20m", point.Band);
    }

    private static Decode Decode(string id, string message) => new()
    {
        Id = id,
        Message = message,
        Snr = -12,
        Mode = "FT8"
    };

    private static IPEndPoint Source(string address) => new(IPAddress.Parse(address), 5000);

    // Processing is batched behind a three-second throttle, so the wait covers that window.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
    }

    private sealed class SettingsWithGrid : IApplicationSettingsService
    {
        private readonly ApplicationSettings _settings = new();

        public SettingsWithGrid()
        {
            _settings.BasicSettings.MyMaidenheadGrid = "OM89";
        }

        public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null) { }

        public void RestoreSettings(object owner) { }

        public ApplicationSettings GetCurrentSettings() => _settings;

        public ApplicationSettings GetCurrentDraftSettingsSnapshot() => _settings;

        public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings)
        {
            draftSettings = _settings;
            return true;
        }
    }

    private sealed class StubDatabaseService : IDatabaseService
    {
        public Task InitDatabaseAsync(string dbPath = "", bool forceInitDatabase = false) => Task.CompletedTask;

        public Task UpgradeDatabaseAsync() => Task.CompletedTask;

        public bool IsUpgradeNeeded() => false;

        public Version GetVersionBeforeUpdate() => new(0, 0, 0);

        public Task<string> GetParentModeAsync(string mode) => Task.FromResult("FT8");

        public Task<CountryDatabase> GetCallsignDetailAsync(string callsign) => Task.FromResult(
            new CountryDatabase { CountryName = "China", Dxcc = "BY", Latitude = 30, Longitude = 114 });

        public Task MarkQsoIgnored(IgnoredQsoDatabase ignoredQso) => Task.CompletedTask;

        public Task<bool> IsQsoIgnored(IgnoredQsoDatabase ignoredQso) => Task.FromResult(false);

        public Task BatchAddOrUpdateCallsignGridAsync(List<CollectedGridDatabase> collectedGrid) =>
            Task.CompletedTask;

        // no stored grid, so the message's own grid is used
        public Task<string?> GetGridByCallsign(string callsign) => Task.FromResult<string?>(null);

        public Task<(int, int)> UpdateCallsignAndCountry(string ctyDat) => Task.FromResult((0, 0));
    }
}
