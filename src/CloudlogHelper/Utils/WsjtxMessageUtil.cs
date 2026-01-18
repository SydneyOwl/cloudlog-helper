using System.Linq;
using System.Text.Json;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Utils;

public class WsjtxMessageUtil
{
    public static string? SerializeWsjtxMessageToJson(WsjtxMessage message)
    {
        return message.MessageType switch
        {
            MessageType.Heartbeat => JsonSerializer.Serialize((Heartbeat)message),
            MessageType.Status => JsonSerializer.Serialize((Status)message),
            MessageType.Decode => JsonSerializer.Serialize((Decode)message),
            MessageType.Clear => JsonSerializer.Serialize((Clear)message),
            MessageType.Reply => JsonSerializer.Serialize((Reply)message),
            MessageType.QSOLogged => JsonSerializer.Serialize((QsoLogged)message),
            MessageType.Close => JsonSerializer.Serialize((Close)message),
            MessageType.Replay => JsonSerializer.Serialize((Replay)message),
            MessageType.HaltTx => JsonSerializer.Serialize((HaltTx)message),
            MessageType.FreeText => JsonSerializer.Serialize((FreeText)message),
            MessageType.WSPRDecode => JsonSerializer.Serialize((WSPRDecode)message),
            MessageType.Location => JsonSerializer.Serialize((Location)message),
            MessageType.LoggedADIF => JsonSerializer.Serialize((LoggedAdif)message),
            MessageType.HighlightCallsign => JsonSerializer.Serialize((HighlightCallsign)message),
            MessageType.SwitchConfiguration => JsonSerializer.Serialize((SwitchConfiguration)message),
            MessageType.Configure => JsonSerializer.Serialize((Configure)message),
            _ => ""
        };
    }
    public static string? ExtractGridFromMessage(string message)
    {
        var messageInfo = message.Trim().Split(" ");
        if (messageInfo.Length < 3) return null;

        var grid = messageInfo[^1].Trim();

        return MaidenheadGridUtil.CheckMaidenhead(grid) ? grid : null;
    }

    public static string? ExtractDeFromMessage(string message)
    {
        var messageInfo = message.Trim().Split(" ");
        if (messageInfo.Length < 3) return null;

        var de = messageInfo[^2].Trim();

        if (string.IsNullOrWhiteSpace(de))
            return null;

        if (de.Any(char.IsLetter) && de.Any(char.IsDigit)) return de;
        return null;
    }
}