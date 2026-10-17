using System;
using System.Globalization;
using CloudlogHelper.Database;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

/// <summary>
///     Pins the thread culture to locales whose decimal separator, group separator or calendar
///     differ from invariant, and asserts that data crossing a machine boundary (cty.dat, ADIF,
///     SQLite rows, rig wire protocols, Cloudlog export ranges) is parsed and formatted the same
///     way regardless. Each test restores the previous culture in a finally.
/// </summary>
public class CultureInvariantParsingTests
{
    private static readonly string[] CommaDecimalCultures = { "de-DE", "fr-FR", "ru-RU" };

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    [InlineData("th-TH")]
    public void CountryDatabase_ParsesCtyDecimals_TheSameUnderEveryCulture(string cultureName)
    {
        var malta = RunInCulture(cultureName,
            () => new CountryDatabase("Sov Mil Order of Malta:   15:  28:  EU:   41.90:   -12.43:    -1.0:  1A:"));
        var tanzania = RunInCulture(cultureName,
            () => new CountryDatabase("Tanzania:                 37:  53:  AF:   -5.75:   -33.92:    -3.0:  5H:"));

        Assert.Equal(15, malta.CqZone);
        Assert.Equal(28, malta.ItuZone);
        Assert.Equal("EU", malta.Continent);
        Assert.Equal("1A", malta.Dxcc);
        Assert.Equal(41.90, malta.Latitude, 4);
        Assert.Equal(12.43, malta.Longitude, 4); // cty stores longitude as east-negative
        Assert.Equal(-1.0, malta.GmtOffset, 4);

        Assert.Equal(-5.75, tanzania.Latitude, 4);
        Assert.Equal(33.92, tanzania.Longitude, 4);
        Assert.Equal(-3.0, tanzania.GmtOffset, 4);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    public void RecordedCallsignDetail_Parse_ReadsInvariantAdifFrequency(string cultureName)
    {
        var detail = RunInCulture(cultureName, () => RecordedCallsignDetail.Parse(CreateAdifLog(), "test"));

        Assert.Equal(14_074_000UL, detail.TXFrequencyInHz);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    public void GenerateAdif_EmitsInvariantFrequencyAndQsoDates(string cultureName)
    {
        var adif = RunInCulture(cultureName, () => CreateDetail().GenerateAdif());

        // Cloudlog/LoTW/eQSL parse these tokens server-side, so a ',' decimal separator or a
        // non-Gregorian year makes the uploaded record unparseable.
        Assert.Contains("<qso_date:8>20260102", adif);
        Assert.Contains("<qso_date_off:8>20260102", adif);
        Assert.Contains("<time_on:6>030405", adif);
        Assert.Contains("<freq:9>14.074000", adif);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    public void AdifLogComparer_MatchesSameQsoAcrossCultures(string cultureName)
    {
        var other = CreateAdifLog();
        other.Freq = "14.074500"; // within DefaultConfigs.AllowedFreqOffsetMHz

        var equal = RunInCulture(cultureName, () => AdifLog.AdifLogComparer.Equals(CreateAdifLog(), other));

        Assert.True(equal);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    public void AdifLogComparer_DoesNotThrowWhenDecimalsUseADot(string cultureName)
    {
        var other = CreateAdifLog();
        other.Freq = "28.074000"; // a different band must not look like the same QSO

        var equal = RunInCulture(cultureName, () => AdifLog.AdifLogComparer.Equals(CreateAdifLog(), other));

        Assert.False(equal);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("th-TH")]
    public void GetExportDateRange_EmitsGregorianIsoDates(string cultureName)
    {
        var (from, to) = RunInCulture(cultureName, () => QsoSyncAssistantUtil.GetExportDateRange(7));

        Assert.Matches("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", from);
        Assert.Matches("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", to);
        // th-TH formats the year as 2569 (Buddhist calendar) unless the calendar is pinned.
        Assert.StartsWith(DateTime.Today.Year.ToString(CultureInfo.InvariantCulture), to);
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to);
        Assert.Equal(DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), from);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void FreqHelper_GetFrequencyStr_KeepsInvariantDecimalSeparator(string cultureName)
    {
        var formatted = RunInCulture(cultureName, () => FreqHelper.GetFrequencyStr(14_074_000));

        Assert.Equal("14.07400MHz", formatted);
        Assert.DoesNotContain(",", formatted);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void IgnoredQsoDatabase_Parse_StoresInvariantFrequency(string cultureName)
    {
        var row = RunInCulture(cultureName, () => IgnoredQsoDatabase.Parse(CreateDetail()));

        Assert.Equal("14.074000", row.Freq);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    public void DetectingStrings_ConfirmTheCultureIsActuallyApplied(string cultureName)
    {
        // Guards the guard: if the host has no ICU data these cultures silently fall back to
        // invariant and every assertion above would pass trivially.
        var floatingPoint = RunInCulture(cultureName, () => (1.5).ToString(CultureInfo.CurrentCulture));

        Assert.NotEqual("1.5", floatingPoint);
        Assert.Contains(",", floatingPoint);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    [InlineData("th-TH")]
    public void RigctldPowerCommand_KeepsADotDecimalSeparator(string cultureName)
    {
        var command = RunInCulture(cultureName,
            () => RigUtils.BuildPowerToMilliwattCommand(0.5f, 14_074_000, "USB"));

        // rigctld reads the arguments with C's strtof, which stops at a decimal comma.
        Assert.Equal(@"\power2mW 0.5 14074000 USB", command);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    public void RigctldPowerCommand_FormatsFractionalPowerInvariantly(string cultureName)
    {
        var command = RunInCulture(cultureName,
            () => RigUtils.BuildPowerToMilliwattCommand(0.35f, 7_074_000, "FT8"));

        Assert.DoesNotContain(",", command);
        Assert.Contains(@"\power2mW 0.35 7074000 FT8", command);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    [InlineData("th-TH")]
    public void AdifExportFileName_StaysGregorianUnderEveryCulture(string cultureName)
    {
        var stamped = new DateTime(2026, 10, 17, 4, 5, 6);

        var fileName = RunInCulture(cultureName, () => AdifUtil.GenerateExportFileName(stamped));

        // The QSO_DATE tokens inside the file are pinned to the Gregorian calendar, so the name
        // must agree with them — th-TH would otherwise stamp the export 25691017.
        Assert.Equal("exported-20261017-040506.adi", fileName);
    }

    private static T RunInCulture<T>(string cultureName, Func<T> body)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static AdifLog CreateAdifLog() => new()
    {
        Call = "K1ABC",
        GridSquare = "FN42",
        Mode = "MFSK",
        SubMode = "FT8",
        RstSent = "-10",
        RstRcvd = "-08",
        QsoDate = "20260102",
        TimeOn = "030405",
        QsoDateOff = "20260102",
        TimeOff = "030505",
        Band = "20m",
        Freq = "14.074000",
        StationCallsign = "BG7AA",
        MyGridSquare = "OL63"
    };

    private static RecordedCallsignDetail CreateDetail() => new()
    {
        DXCall = "K1ABC",
        DXGrid = "FN42",
        ReportSent = "-10",
        ReportReceived = "-08",
        DateTimeOn = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        DateTimeOff = new DateTime(2026, 1, 2, 3, 5, 5, DateTimeKind.Utc),
        TXFrequencyInHz = 14_074_000,
        TXFrequencyInMeters = "20m",
        Mode = "MFSK",
        ParentMode = "",
        MyCall = "BG7AA",
        MyGrid = "OL63",
        Comments = "test"
    };
}
