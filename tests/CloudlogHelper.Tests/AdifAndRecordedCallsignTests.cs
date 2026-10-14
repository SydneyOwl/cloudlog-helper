using CloudlogHelper.Database;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Tests;

public class AdifAndRecordedCallsignTests
{
    [Theory]
    [InlineData("WSJT-X - 20m", "WSJT-X - 20m")]
    [InlineData("WSJT-X - 40m", "WSJT-X - 40m")]
    [InlineData("JTDX", "JTDX")]
    [InlineData("", WsjtxMessageUtil.UnknownClient)]
    [InlineData(null, WsjtxMessageUtil.UnknownClient)]
    public void GenerateCallsignDetail_KeepsFullClientKey(string? id, string expectedClientId)
    {
        var qlo = new QsoLogged
        {
            Id = id!,
            DXCall = "K1ABC",
            DXGrid = "FN42",
            TXFrequencyInHz = 14_074_000,
            Mode = "FT8",
            DateTimeOn = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            DateTimeOff = new DateTime(2026, 1, 2, 3, 5, 5, DateTimeKind.Utc)
        };

        var detail = RecordedCallsignDetail.GenerateCallsignDetail(new CountryDatabase(), qlo);

        Assert.Equal(expectedClientId, detail.ClientId);
    }

    [Fact]
    public void GenerateAdifLog_IncludesSubModeAndCommentOnlyWhenPresent()
    {
        var withOptionalFields = AdifUtil.GenerateAdifLog(CreateAdifLog(subMode: "FT8", comment: "nice qso"));
        var withoutOptionalFields = AdifUtil.GenerateAdifLog(CreateAdifLog(subMode: "", comment: ""));

        Assert.Contains("<submode:3>FT8", withOptionalFields);
        Assert.Contains("<comment:8>nice qso", withOptionalFields);
        Assert.DoesNotContain("<submode:", withoutOptionalFields);
        Assert.DoesNotContain("<comment:", withoutOptionalFields);
    }

    [Fact]
    public void GenerateAdif_UsesParentModeAsMode_AndModeAsSubMode()
    {
        var detail = CreateRecordedCallsignDetail();
        detail.Mode = "FT8";
        detail.ParentMode = "MFSK";

        var adif = detail.GenerateAdif();

        Assert.Contains("<mode:4>MFSK", adif);
        Assert.Contains("<submode:3>FT8", adif);
    }

    [Theory]
    [InlineData(UploadStatus.Pending, true)]
    [InlineData(UploadStatus.Fail, true)]
    [InlineData(UploadStatus.Ignored, true)]
    [InlineData(UploadStatus.Success, false)]
    [InlineData(UploadStatus.Uploading, false)]
    [InlineData(UploadStatus.Retrying, false)]
    public void IsUploadable_ReturnsTrueOnlyForQueueableStatuses(UploadStatus status, bool expected)
    {
        var detail = new RecordedCallsignDetail { UploadStatus = status };

        Assert.Equal(expected, detail.IsUploadable());
    }

    [Fact]
    public void TryMarkInUploadQueue_IsExclusiveUntilCleared()
    {
        var detail = new RecordedCallsignDetail();

        Assert.True(detail.TryMarkInUploadQueue());
        Assert.False(detail.TryMarkInUploadQueue());

        detail.ClearInUploadQueue();

        Assert.True(detail.TryMarkInUploadQueue());
    }

    [Fact]
    public void TryMarkInUploadQueue_UnderConcurrentCallers_OnlyOneWins()
    {
        var detail = new RecordedCallsignDetail();
        const int callers = 64;
        var winners = 0;
        using var barrier = new Barrier(callers);

        Parallel.For(0, callers, _ =>
        {
            barrier.SignalAndWait();
            if (detail.TryMarkInUploadQueue()) Interlocked.Increment(ref winners);
        });

        Assert.Equal(1, winners);
    }

    private static AdifLog CreateAdifLog(string subMode, string comment)
    {
        return new AdifLog
        {
            Call = "K1ABC",
            GridSquare = "FN42",
            Mode = "MFSK",
            SubMode = subMode,
            RstSent = "-10",
            RstRcvd = "-08",
            QsoDate = "20260102",
            TimeOn = "030405",
            QsoDateOff = "20260102",
            TimeOff = "030505",
            Band = "20m",
            Freq = "14.074000",
            StationCallsign = "BG7AA",
            MyGridSquare = "OL63",
            Comment = comment
        };
    }

    private static RecordedCallsignDetail CreateRecordedCallsignDetail()
    {
        return new RecordedCallsignDetail
        {
            DXCall = "K1ABC",
            DXGrid = "FN42",
            ReportSent = "-10",
            ReportReceived = "-08",
            DateTimeOn = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            DateTimeOff = new DateTime(2026, 1, 2, 3, 5, 5, DateTimeKind.Utc),
            TXFrequencyInHz = 14_074_000,
            TXFrequencyInMeters = "20m",
            MyCall = "BG7AA",
            MyGrid = "OL63",
            Comments = "test"
        };
    }
}
