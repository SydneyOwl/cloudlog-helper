using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using SydneyOwl.CLHProto.Plugin;
using WsjtxFreeText = WsjtxUtilsPatch.WsjtxMessages.Messages.FreeText;
using WsjtxHeartbeat = WsjtxUtilsPatch.WsjtxMessages.Messages.Heartbeat;
using WsjtxLocation = WsjtxUtilsPatch.WsjtxMessages.Messages.Location;
using WsjtxSchemaVersion = WsjtxUtilsPatch.WsjtxMessages.Messages.SchemaVersion;

namespace CloudlogHelper.Tests;

public class PbMsgConverterTests
{
    [Fact]
    public void ToPbRigData_MapsRadioFieldsAndProvider()
    {
        var radio = new RadioData
        {
            RigName = "IC-7300",
            FrequencyTx = 14_074_000,
            ModeTx = "FT8",
            FrequencyRx = 7_074_000,
            ModeRx = "LSB",
            IsSplit = true,
            Power = 50
        };

        var result = PbMsgConverter.ToPbRigData("hamlib", radio);

        Assert.Equal("hamlib", result.Provider);
        Assert.Equal("IC-7300", result.RigName);
        Assert.Equal(14_074_000UL, result.Frequency);
        Assert.Equal("FT8", result.Mode);
        Assert.Equal(7_074_000UL, result.FrequencyRx);
        Assert.True(result.Split);
        Assert.Equal(50U, result.Power);
        Assert.NotNull(result.Timestamp);
    }

    [Fact]
    public void ToPbWsjtxMessage_MapsHeartbeatHeaderAndPayload()
    {
        var heartbeat = new WsjtxHeartbeat
        {
            Id = "WSJT-X",
            MagicNumber = 0xadbccbda,
            SchemaVersion = (WsjtxSchemaVersion)3,
            MaximumSchemaNumber = (WsjtxSchemaVersion)4,
            Version = "2.7.0",
            Revision = "abc123"
        };

        var result = PbMsgConverter.ToPbWsjtxMessage(heartbeat);

        Assert.NotNull(result);
        Assert.Equal(MessageType.Heartbeat, result.Header.Type);
        Assert.Equal("WSJT-X", result.Header.Id);
        Assert.Equal(4U, result.Heartbeat.MaxSchemaNumber);
        Assert.Equal("2.7.0", result.Heartbeat.Version);
        Assert.Equal("abc123", result.Heartbeat.Revision);
    }

    [Fact]
    public void ToPbWsjtxMessage_MapsFreeTextAndLocationPayloads()
    {
        var freeText = PbMsgConverter.ToPbWsjtxMessage(new WsjtxFreeText
        {
            Id = "client",
            Text = "CQ TEST",
            Send = true
        });
        var location = PbMsgConverter.ToPbWsjtxMessage(new WsjtxLocation
        {
            Id = "client",
            LocationGridSquare = "OL63"
        });

        Assert.NotNull(freeText);
        Assert.Equal(MessageType.FreeText, freeText.Header.Type);
        Assert.Equal("CQ TEST", freeText.FreeText.Text);
        Assert.True(freeText.FreeText.Send);

        Assert.NotNull(location);
        Assert.Equal(MessageType.Location, location.Header.Type);
        Assert.Equal("OL63", location.Location.Location_);
    }
}
