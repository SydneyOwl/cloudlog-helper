using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using Xunit.Abstractions;

namespace CloudlogHelper.Tests;

public class AdifLogTests
{
    [Fact]
    public void GenerateAdifLog_WithAllFields_ReturnsCorrectAdifFormat()
    {
        // Arrange
        var log = new AdifLog
        {
            Call = "N0CALL",
            GridSquare = "FN31pr",
            Mode = "FT8",
            SubMode = "",
            RstSent = "599",
            RstRcvd = "599",
            QsoDate = "20231015",
            TimeOn = "1230",
            QsoDateOff = "20231015",
            TimeOff = "1232",
            Band = "20m",
            Freq = "14.074",
            StationCallsign = "MYCALL",
            MyGridSquare = "FN32ab",
            Comment = "Nice QSO"
        };

        // Act
        var result = AdifUtil.GenerateAdifLog(log);

        // Assert
        var expected = "<call:6>N0CALL " +
                       "<gridsquare:6>FN31pr " +
                       "<mode:3>FT8 " +
                       "<rst_sent:3>599 " +
                       "<rst_rcvd:3>599 " +
                       "<qso_date:8>20231015 " +
                       "<time_on:4>1230 " +
                       "<qso_date_off:8>20231015 " +
                       "<time_off:4>1232 " +
                       "<band:3>20m " +
                       "<freq:6>14.074 " +
                       "<station_callsign:6>MYCALL " +
                       "<my_gridsquare:6>FN32ab " +
                       "<comment:8>Nice QSO " +
                       "<eor>";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GenerateAdifLog_WithSubMode_IncludesSubModeField()
    {
        // Arrange
        var log = new AdifLog
        {
            Call = "N0CALL",
            GridSquare = "FN31pr",
            Mode = "SSB",
            SubMode = "USB",
            RstSent = "59",
            RstRcvd = "57",
            QsoDate = "20231015",
            TimeOn = "1230",
            QsoDateOff = "20231015",
            TimeOff = "1232",
            Band = "20m",
            Freq = "14.200",
            StationCallsign = "MYCALL",
            MyGridSquare = "FN32ab",
            Comment = ""
        };

        // Act
        var result = AdifUtil.GenerateAdifLog(log);

        // Assert
        Assert.Contains("<submode:3>USB", result);
    }

    [Fact]
    public void GenerateAdifLog_WithoutSubMode_ExcludesSubModeField()
    {
        // Arrange
        var log = new AdifLog
        {
            Call = "N0CALL",
            GridSquare = "FN31pr",
            Mode = "FT8",
            SubMode = "",
            RstSent = "599",
            RstRcvd = "599",
            QsoDate = "20231015",
            TimeOn = "1230",
            QsoDateOff = "20231015",
            TimeOff = "1232",
            Band = "20m",
            Freq = "14.074",
            StationCallsign = "MYCALL",
            MyGridSquare = "FN32ab",
            Comment = ""
        };

        // Act
        var result = AdifUtil.GenerateAdifLog(log);

        // Assert
        Assert.DoesNotContain("submode", result);
    }

    [Fact]
    public void GenerateAdifLog_WithoutComment_ExcludesCommentField()
    {
        // Arrange
        var log = new AdifLog
        {
            Call = "N0CALL",
            GridSquare = "FN31pr",
            Mode = "FT8",
            SubMode = "",
            RstSent = "599",
            RstRcvd = "599",
            QsoDate = "20231015",
            TimeOn = "1230",
            QsoDateOff = "20231015",
            TimeOff = "1232",
            Band = "20m",
            Freq = "14.074",
            StationCallsign = "MYCALL",
            MyGridSquare = "FN32ab",
            Comment = ""
        };

        // Act
        var result = AdifUtil.GenerateAdifLog(log);

        // Assert
        Assert.DoesNotContain("comment", result);
    }

    [Fact]
    public void GenerateAdifLog_WithNullComment_ExcludesCommentField()
    {
        // Arrange
        var log = new AdifLog
        {
            Call = "N0CALL",
            GridSquare = "FN31pr",
            Mode = "FT8",
            SubMode = "",
            RstSent = "599",
            RstRcvd = "599",
            QsoDate = "20231015",
            TimeOn = "1230",
            QsoDateOff = "20231015",
            TimeOff = "1232",
            Band = "20m",
            Freq = "14.074",
            StationCallsign = "MYCALL",
            MyGridSquare = "FN32ab",
            Comment = null
        };

        var result = AdifUtil.GenerateAdifLog(log);
        Assert.DoesNotContain("comment", result);
    }

    [Fact]
    public void GenerateAdifLog_WithEmptyStringFields_HandlesCorrectly()
    {
        // Arrange
        var log = new AdifLog
        {
            Call = "",
            GridSquare = "",
            Mode = "",
            SubMode = "",
            RstSent = "",
            RstRcvd = "",
            QsoDate = "",
            TimeOn = "",
            QsoDateOff = "",
            TimeOff = "",
            Band = "",
            Freq = "",
            StationCallsign = "",
            MyGridSquare = "",
            Comment = ""
        };

        // Act
        var result = AdifUtil.GenerateAdifLog(log);

        // Assert
        Assert.Contains("<call:0>", result);
        Assert.Contains("<gridsquare:0>", result);
        Assert.Contains("<mode:0>", result);
        Assert.DoesNotContain("submode", result);
        Assert.DoesNotContain("comment", result);
    }

    [Fact]
    public void GenerateAdifLog_EndsWithEor()
    {
        // Arrange
        var log = new AdifLog
        {
            Call = "N0CALL",
            GridSquare = "FN31pr",
            Mode = "FT8",
            SubMode = "",
            RstSent = "599",
            RstRcvd = "599",
            QsoDate = "20231015",
            TimeOn = "1230",
            QsoDateOff = "20231015",
            TimeOff = "1232",
            Band = "20m",
            Freq = "14.074",
            StationCallsign = "MYCALL",
            MyGridSquare = "FN32ab",
            Comment = ""
        };

        // Act
        var result = AdifUtil.GenerateAdifLog(log);

        // Assert
        Assert.EndsWith("<eor>", result);
    }
}