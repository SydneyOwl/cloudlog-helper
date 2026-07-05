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
