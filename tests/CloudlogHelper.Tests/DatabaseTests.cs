using CloudlogHelper.Database;
using CloudlogHelper.Services.Interfaces;

namespace CloudlogHelper.Tests;

public class DatabaseTests : IClassFixture<DatabaseFixture>
{
    private readonly IDatabaseService _databaseService;

    public DatabaseTests(DatabaseFixture fixture)
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
    [InlineData("JA1XXX", "JA", "AS")]
    [InlineData("DL1XXX", "DL", "EU")]
    [InlineData("ZS1XXX", "ZS", "AF")]
    [InlineData("LU1XXX", "LU", "SA")]
    public async Task GetCallsignDetailAsync_ValidCallsign_ReturnsCorrectDxccAndContinent(
        string callsign, string expectedDxcc, string expectedContinent)
    {
        var result = await _databaseService.GetCallsignDetailAsync(callsign);
        Assert.NotNull(result);
        Assert.Equal(expectedDxcc, result.Dxcc);
        Assert.Equal(expectedContinent, result.Continent);
    }

    [Fact]
    public async Task GetCallsignDetailAsync_EmptyCallsign_ReturnsUnknown()
    {
        var result = await _databaseService.GetCallsignDetailAsync("");
        Assert.NotNull(result);
        Assert.Equal("Unknown", result.CountryName);
    }

    [Fact]
    public async Task GetCallsignDetailAsync_UnknownCallsign_ReturnsEmptyCountry()
    {
        var result = await _databaseService.GetCallsignDetailAsync("QQ0ZZZ");
        Assert.NotNull(result);
        Assert.True(string.IsNullOrEmpty(result.Dxcc));
    }

    [Theory]
    [InlineData("FT8", "")]
    [InlineData("FT4", "MFSK")]
    [InlineData("JT65", "")]
    [InlineData("PSK31", "PSK")]
    [InlineData("JT9", "")]
    [InlineData("JS8", "MFSK")]
    [InlineData("RTTY", "")]
    [InlineData("SSB", "")]
    public async Task GetParentModeAsync_ReturnsCorrectResult(string mode, string expectedParentMode)
    {
        var result = await _databaseService.GetParentModeAsync(mode);
        Assert.Equal(expectedParentMode, result);
    }

    [Fact]
    public async Task MarkQsoIgnored_NewEntry_ShouldPersist()
    {
        var ignoredQso = new IgnoredQsoDatabase
        {
            De = "TEST1",
            Dx = "TEST2",
            Freq = "14.074000",
            FinalMode = "FT8",
            RstSent = "599",
            RstRecv = "599",
            QsoStartTime = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc)
        };

        await _databaseService.MarkQsoIgnored(ignoredQso);

        var isIgnored = await _databaseService.IsQsoIgnored(ignoredQso);
        Assert.True(isIgnored);
    }

    [Fact]
    public async Task IsQsoIgnored_NonExistentEntry_ReturnsFalse()
    {
        var ignoredQso = new IgnoredQsoDatabase
        {
            De = "NONEXIST",
            Dx = "CALL",
            Freq = "7.074000",
            FinalMode = "FT8",
            RstSent = "599",
            RstRecv = "599",
            QsoStartTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var isIgnored = await _databaseService.IsQsoIgnored(ignoredQso);
        Assert.False(isIgnored);
    }

    [Fact]
    public async Task BatchAddOrUpdateCallsignGrid_NewGrid_ShouldPersist()
    {
        var grids = new List<CollectedGridDatabase>
        {
            new() { Callsign = "BG5XXX", GridSquare = "PM01" }
        };

        await _databaseService.BatchAddOrUpdateCallsignGridAsync(grids);

        var result = await _databaseService.GetGridByCallsign("BG5XXX");
        Assert.Equal("PM01", result);
    }

    [Fact]
    public async Task GetGridByCallsign_UnknownCallsign_ReturnsNull()
    {
        var result = await _databaseService.GetGridByCallsign("ZZ0ZZZ");
        Assert.Null(result);
    }
}