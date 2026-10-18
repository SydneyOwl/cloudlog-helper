using System.Net;
using CloudlogHelper.Database;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Tests;

public class AdifAndRecordedCallsignTests
{
    [Theory]
    [InlineData("WSJT-X - 20m", "10.0.0.7", "WSJT-X - 20m")]
    [InlineData("WSJT-X - 40m", "10.0.0.7", "WSJT-X - 40m")]
    [InlineData("JTDX", "10.0.0.7", "JTDX")]
    [InlineData("", "10.0.0.7", "10.0.0.7")]
    [InlineData(null, "10.0.0.7", "10.0.0.7")]
    [InlineData("", null, WsjtxMessageUtil.UnknownClient)]
    [InlineData(null, null, WsjtxMessageUtil.UnknownClient)]
    public void GenerateCallsignDetail_KeepsFullClientKey(string? id, string? sourceAddress, string expectedClientId)
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

        var detail = RecordedCallsignDetail.GenerateCallsignDetail(new CountryDatabase(), qlo,
            Source(sourceAddress));

        Assert.Equal(expectedClientId, detail.ClientId);
    }

    [Fact]
    public void GenerateCallsignDetail_BlankId_RecordsSourceAddressAlongsideTheFallbackKey()
    {
        var source = Source("10.0.0.7");
        var detail = Generate("", source);

        Assert.Equal("10.0.0.7", detail.ClientId);
        Assert.Equal("10.0.0.7", detail.SourceAddress);
    }

    [Fact]
    public void GenerateCallsignDetail_NonBlankId_KeepsTheIdButStillRecordsSourceAddress()
    {
        var detail = Generate("WSJT-X - 20m", Source("10.0.0.7"));

        Assert.Equal("WSJT-X - 20m", detail.ClientId);
        Assert.Equal("10.0.0.7", detail.SourceAddress);
    }

    [Fact]
    public void GenerateCallsignDetail_IgnoresSourcePort()
    {
        var fromLowPort = Generate("", new IPEndPoint(IPAddress.Parse("10.0.0.7"), 1));
        var fromHighPort = Generate("", new IPEndPoint(IPAddress.Parse("10.0.0.7"), 6999));

        // A WSJT-X instance picks a new ephemeral source port every start, so the port must not
        // fragment one machine into two sessions.
        Assert.Equal(fromLowPort.ClientId, fromHighPort.ClientId);
        Assert.Equal("10.0.0.7", fromHighPort.SourceAddress);
    }

    [Fact]
    public void GenerateCallsignDetail_NoSource_LeavesSourceAddressNull()
    {
        Assert.Null(Generate("WSJT-X - 20m", null).SourceAddress);
    }

    private static RecordedCallsignDetail Generate(string? id, EndPoint? source) =>
        RecordedCallsignDetail.GenerateCallsignDetail(new CountryDatabase(),
            new QsoLogged
            {
                Id = id!,
                DXCall = "K1ABC",
                DXGrid = "FN42",
                TXFrequencyInHz = 14_074_000,
                Mode = "FT8",
                DateTimeOn = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                DateTimeOff = new DateTime(2026, 1, 2, 3, 5, 5, DateTimeKind.Utc)
            }, source);

    private static IPEndPoint? Source(string? address) =>
        address is null ? null : new IPEndPoint(IPAddress.Parse(address), 5000);

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

        // Dedicated threads, not Parallel.For: a Barrier of 64 participants on the thread pool
        // starves it (slow thread injection) and turns this into a multi-second test.
        using var start = new ManualResetEventSlim(false);
        var threads = new Thread[callers];

        for (var i = 0; i < callers; i++)
        {
            threads[i] = new Thread(() =>
            {
                start.Wait();
                if (detail.TryMarkInUploadQueue()) Interlocked.Increment(ref winners);
            });
            threads[i].Start();
        }

        start.Set();
        foreach (var thread in threads) thread.Join();

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
