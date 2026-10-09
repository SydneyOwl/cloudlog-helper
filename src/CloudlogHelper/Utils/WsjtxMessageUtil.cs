using System;
using System.Linq;
using System.Text.Json;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Utils;

public class WsjtxMessageUtil
{
    /// <summary>
    ///     Bucket used when a client sends an empty/missing Id.
    /// </summary>
    public const string UnknownClient = "Unknown";

    /// <summary>
    ///     Canonical registry key for a client, based on the full UDP Id so that
    ///     e.g. "WSJT-X - 20m" and "WSJT-X - 40m" never collide.
    /// </summary>
    public static string GetClientKey(string? id)
    {
        return string.IsNullOrWhiteSpace(id) ? UnknownClient : id;
    }

    /// <summary>
    ///     Short, human friendly label for a client. Mirrors gridtracker's short-instance
    ///     naming: take the last " - " segment when present, otherwise the raw Id.
    /// </summary>
    public static string GetClientDisplayName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return UnknownClient;

        var parts = id.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1].Trim() : id;
    }

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
        var messageInfo = message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (messageInfo.Length < 3) return null;

        var grid = messageInfo[^1].Trim();

        return MaidenheadGridUtil.CheckMaidenhead(grid) ? grid : null;
    }

    public static string? ExtractDeFromMessage(string message)
    {
        var messageInfo = message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (messageInfo.Length < 3) return null;

        var de = messageInfo[^2].Trim();

        if (string.IsNullOrWhiteSpace(de))
            return null;

        if (de.Any(char.IsLetter) && de.Any(char.IsDigit)) return de;
        return null;
    }
}
