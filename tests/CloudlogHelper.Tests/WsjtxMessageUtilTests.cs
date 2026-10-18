using System.Text.Json;
using CloudlogHelper.Utils;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Tests;

public class WsjtxMessageUtilTests
{
    [Theory]
    [InlineData("CQ K1ABC FN42", "FN42")]
    [InlineData("  CQ   BG7AA   OL63  ", "OL63")]
    [InlineData("CQ K1ABC NOTGRID", null)]
    [InlineData("TOO-SHORT", null)]
    public void ExtractGridFromMessage_ReturnsLastToken_WhenItIsValidGrid(string message, string? expected)
    {
        Assert.Equal(expected, WsjtxMessageUtil.ExtractGridFromMessage(message));
    }

    [Theory]
    [InlineData("CQ K1ABC FN42", "K1ABC")]
    [InlineData("  CQ   BG7AA   OL63  ", "BG7AA")]
    [InlineData("CQ TESTONLY FN42", null)]
    [InlineData("TOO-SHORT", null)]
    public void ExtractDeFromMessage_ReturnsCallsignLikeTokenBeforeGrid(string message, string? expected)
    {
        Assert.Equal(expected, WsjtxMessageUtil.ExtractDeFromMessage(message));
    }

    [Theory]
    [InlineData("WSJT-X - 20m", "WSJT-X - 20m")]
    [InlineData("JTDX", "JTDX")]
    [InlineData("  WSJT-X  ", "  WSJT-X  ")]
    [InlineData(null, WsjtxMessageUtil.UnknownClient)]
    [InlineData("", WsjtxMessageUtil.UnknownClient)]
    [InlineData("   ", WsjtxMessageUtil.UnknownClient)]
    public void GetClientKey_ReturnsFullIdOrUnknown(string? id, string expected)
    {
        Assert.Equal(expected, WsjtxMessageUtil.GetClientKey(id));
    }

    [Theory]
    [InlineData("WSJT-X - 20m", "20m")]
    [InlineData("WSJT-X - 40m", "40m")]
    [InlineData("JTDX", "JTDX")]
    [InlineData(null, WsjtxMessageUtil.UnknownClient)]
    [InlineData("", WsjtxMessageUtil.UnknownClient)]
    public void GetClientDisplayName_UsesLastSegmentOrRawId(string? id, string expected)
    {
        Assert.Equal(expected, WsjtxMessageUtil.GetClientDisplayName(id));
    }

    [Theory]
    [InlineData("WSJT-X - 20m", "10.0.0.5", "WSJT-X - 20m")]
    [InlineData("  WSJT-X  ", "10.0.0.5", "  WSJT-X  ")]
    [InlineData("", "10.0.0.5", "10.0.0.5")]
    [InlineData("   ", "10.0.0.5", "10.0.0.5")]
    [InlineData(null, "10.0.0.5", "10.0.0.5")]
    [InlineData("", null, WsjtxMessageUtil.UnknownClient)]
    [InlineData(null, null, WsjtxMessageUtil.UnknownClient)]
    public void GetClientKey_NonBlankIdWins_ThenSourceAddress_ThenUnknown(string? id, string? sourceAddress,
        string expected)
    {
        Assert.Equal(expected, WsjtxMessageUtil.GetClientKey(id, Endpoint(sourceAddress)));
    }

    [Fact]
    public void GetClientKey_BlankId_IgnoresSourcePort()
    {
        var fromLowPort = WsjtxMessageUtil.GetClientKey("", Endpoint("10.0.0.5", port: 1));
        var fromHighPort = WsjtxMessageUtil.GetClientKey("", Endpoint("10.0.0.5", port: 6999));

        Assert.Equal(fromLowPort, fromHighPort);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.100")]
    [InlineData("::1")]
    public void GetSourceAddress_ExtractsIpWithoutPort(string address)
    {
        Assert.Equal(address, WsjtxMessageUtil.GetSourceAddress(Endpoint(address, port: 54321)));
    }

    [Fact]
    public void GetSourceAddress_ReturnsNullForNullOrNonIpEndPoint()
    {
        Assert.Null(WsjtxMessageUtil.GetSourceAddress(null));
        Assert.Null(WsjtxMessageUtil.GetSourceAddress(new NonIpEndPoint()));
    }

    private static System.Net.EndPoint Endpoint(string? address, int port = 5000) =>
        address is null ? null! : new System.Net.IPEndPoint(System.Net.IPAddress.Parse(address), port);

    private sealed class NonIpEndPoint : System.Net.EndPoint
    {
    }

    [Fact]
    public void SerializeWsjtxMessageToJson_SerializesConcreteMessagePayload()
    {
        var message = new FreeText
        {
            Id = "WSJT-X",
            Text = "CQ TEST",
            Send = true
        };

        var json = WsjtxMessageUtil.SerializeWsjtxMessageToJson(message);
        using var doc = JsonDocument.Parse(json!);

        Assert.Equal("CQ TEST", doc.RootElement.GetProperty(nameof(FreeText.Text)).GetString());
        Assert.True(doc.RootElement.GetProperty(nameof(FreeText.Send)).GetBoolean());
    }
}
