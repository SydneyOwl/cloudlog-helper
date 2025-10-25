using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;

namespace CloudlogHelper.Tests;

public class CallsignDataTests : IClassFixture<DatabaseFixture>
{
    private readonly IDatabaseService _databaseService;

    public CallsignDataTests(DatabaseFixture fixture)
    {
        _databaseService = fixture.DatabaseService;
    }

    [Theory]
    [InlineData("BG5XXX", "BY", "AS")]
    [InlineData("VK5XXX", "VK", "OC")]
    [InlineData("AJ6XX", "K", "NA")]
    [InlineData("PP8XX", "PY", "SA")]
    [InlineData("MW0XXX", "GW", "EU")]
    [InlineData("VP2EEE", "VP2E", "NA")]
    public async Task TestVerifyCallsignInfo_ReturnsCorrectResult(string callsign, string dxcc, string continent)
    {
        var callsignDetailAsync = await _databaseService.GetCallsignDetailAsync(callsign);
        Assert.NotNull(callsignDetailAsync);
        Assert.Equal(dxcc, callsignDetailAsync.Dxcc);
        Assert.Equal(continent, callsignDetailAsync.Continent);
    }

    [Theory]
    [InlineData("FT8", "")]
    [InlineData("FT4", "MFSK")]
    [InlineData("JT65", "")]
    [InlineData("PSK31", "PSK")]
    [InlineData("JT9", "")]
    [InlineData("JS8", "MFSK")]
    public async Task TestGetDigiModeParentMode_ReturnsCorrectResult(string mode, string parentMode)
    {
        var pm = await _databaseService.GetParentModeAsync(mode);
        Assert.Equal(parentMode, pm);
    }
}

public class DatabaseFixture : IAsyncLifetime
{
    public IDatabaseService DatabaseService { get; private set; }

    public async Task InitializeAsync()
    {
        DatabaseService = new DatabaseService();
        await DatabaseService.InitDatabaseAsync(":memory:", true);
        await DatabaseService.UpgradeDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}