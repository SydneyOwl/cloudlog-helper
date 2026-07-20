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

    [Fact]
    public async Task BatchAddOrUpdateCallsignGrid_UpdateExisting_ShouldOverwrite()
    {
        var callsign = "BG5XXX";
        await _databaseService.BatchAddOrUpdateCallsignGridAsync(new List<CollectedGridDatabase>
        {
            new() { Callsign = callsign, GridSquare = "PM01" }
        });
        await _databaseService.BatchAddOrUpdateCallsignGridAsync(new List<CollectedGridDatabase>
        {
            new() { Callsign = callsign, GridSquare = "OM44" }
        });

        var result = await _databaseService.GetGridByCallsign(callsign);
        Assert.Equal("OM44", result);
    }

    [Fact]
    public async Task BatchAddOrUpdateCallsignGrid_EmptyList_ShouldNotThrow()
    {
        await _databaseService.BatchAddOrUpdateCallsignGridAsync(new List<CollectedGridDatabase>());
    }

    [Fact]
    public async Task BatchAddOrUpdateCallsignGrid_MultipleEntries_ShouldPersistAll()
    {
        var grids = new List<CollectedGridDatabase>
        {
            new() { Callsign = "JA1XXX", GridSquare = "PM95" },
            new() { Callsign = "DL1XXX", GridSquare = "JO40" },
            new() { Callsign = "ZS1XXX", GridSquare = "JF96" }
        };
        await _databaseService.BatchAddOrUpdateCallsignGridAsync(grids);

        Assert.Equal("PM95", await _databaseService.GetGridByCallsign("JA1XXX"));
        Assert.Equal("JO40", await _databaseService.GetGridByCallsign("DL1XXX"));
        Assert.Equal("JF96", await _databaseService.GetGridByCallsign("ZS1XXX"));
    }

    [Fact]
    public async Task MarkQsoIgnored_DuplicateEntry_ShouldNotThrow()
    {
        var ignoredQso = new IgnoredQsoDatabase
        {
            De = "DUP1", Dx = "DUP2", Freq = "21.074000",
            FinalMode = "FT8", RstSent = "599", RstRecv = "599",
            QsoStartTime = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc)
        };
        await _databaseService.MarkQsoIgnored(ignoredQso);
        await _databaseService.MarkQsoIgnored(ignoredQso);

        Assert.True(await _databaseService.IsQsoIgnored(ignoredQso));
    }

    [Fact]
    public async Task IsQsoIgnored_SimilarFreqWithinTolerance_ReturnsTrue()
    {
        var original = new IgnoredQsoDatabase
        {
            De = "SIM1", Dx = "SIM2", Freq = "14.074000",
            FinalMode = "FT8", RstSent = "599", RstRecv = "599",
            QsoStartTime = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc)
        };
        await _databaseService.MarkQsoIgnored(original);

        var similar = new IgnoredQsoDatabase
        {
            De = "SIM1", Dx = "SIM2", Freq = "14.074050",
            FinalMode = "FT8", RstSent = "599", RstRecv = "599",
            QsoStartTime = new DateTime(2026, 7, 20, 12, 0, 5, DateTimeKind.Utc)
        };
        Assert.True(await _databaseService.IsQsoIgnored(similar));
    }

    [Fact]
    public async Task IsQsoIgnored_FreqOutsideTolerance_ReturnsFalse()
    {
        var original = new IgnoredQsoDatabase
        {
            De = "FAR1", Dx = "FAR2", Freq = "14.074000",
            FinalMode = "FT8", RstSent = "599", RstRecv = "599",
            QsoStartTime = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc)
        };
        await _databaseService.MarkQsoIgnored(original);

        var far = new IgnoredQsoDatabase
        {
            De = "FAR1", Dx = "FAR2", Freq = "14.500000",
            FinalMode = "FT8", RstSent = "599", RstRecv = "599",
            QsoStartTime = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc)
        };
        Assert.False(await _databaseService.IsQsoIgnored(far));
    }

    [Fact]
    public async Task IsQsoIgnored_TimeOutsideTolerance_ReturnsFalse()
    {
        var original = new IgnoredQsoDatabase
        {
            De = "LATE1", Dx = "LATE2", Freq = "14.074000",
            FinalMode = "FT8", RstSent = "599", RstRecv = "599",
            QsoStartTime = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc)
        };
        await _databaseService.MarkQsoIgnored(original);

        var late = new IgnoredQsoDatabase
        {
            De = "LATE1", Dx = "LATE2", Freq = "14.074000",
            FinalMode = "FT8", RstSent = "599", RstRecv = "599",
            QsoStartTime = new DateTime(2026, 7, 20, 13, 0, 0, DateTimeKind.Utc)
        };
        Assert.False(await _databaseService.IsQsoIgnored(late));
    }

    [Fact]
    public async Task IsQsoIgnored_NullStartTime_ReturnsFalse()
    {
        var ignoredQso = new IgnoredQsoDatabase
        {
            De = "NT1", Dx = "NT2", Freq = "14.074000",
            FinalMode = "FT8", RstSent = "599", RstRecv = "599",
            QsoStartTime = null
        };
        Assert.False(await _databaseService.IsQsoIgnored(ignoredQso));
    }

    [Fact]
    public async Task GetParentModeAsync_UnknownMode_ReturnsEmpty()
    {
        var result = await _databaseService.GetParentModeAsync("NONEXISTENT_MODE");
        Assert.Equal("", result);
    }

    [Fact]
    public async Task GetCallsignDetailAsync_LongestPrefixMatch_ReturnsCorrectDxcc()
    {
        var result = await _databaseService.GetCallsignDetailAsync("VP2EXXX");
        Assert.NotNull(result);
        Assert.Equal("VP2E", result.Dxcc);
    }

    [Fact]
    public async Task UpdateCallsignAndCountry_WithEmbeddedData_ShouldSucceed()
    {
        var (countryCount, callsignCount) = await _databaseService.UpdateCallsignAndCountry(null!);
        Assert.True(countryCount > 173);
        Assert.True(callsignCount > 3539);
    }

    [Fact]
    public async Task UpdateCallsignAndCountry_AfterUpdate_CallsignLookupStillWorks()
    {
        await _databaseService.UpdateCallsignAndCountry(null!);

        var result = await _databaseService.GetCallsignDetailAsync("JA1XXX");
        Assert.NotNull(result);
        Assert.Equal("JA", result.Dxcc);
        Assert.Equal("AS", result.Continent);
    }
}