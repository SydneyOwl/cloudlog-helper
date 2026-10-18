using System;
using System.Linq;
using System.Net;
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
    ///     Synthetic client key for QSOs that were not received over UDP (e.g. imported by the
    ///     QSO sync assistant). Kept separate from real WSJT-X client ids.
    /// </summary>
    public const string LocalClient = "LOCAL";

    /// <summary>
    ///     Canonical registry key for a client, based on the full UDP Id so that
    ///     e.g. "WSJT-X - 20m" and "WSJT-X - 40m" never collide.
    /// </summary>
    /// <remarks>
    ///     The single place a client key is derived: both <see cref="ClientSession.Id"/> and
    ///     <see cref="RecordedCallsignDetail.ClientId"/> come from here, so the tab filter's key
    ///     and the QSO's own key can never disagree. An empty Id falls back to the UDP source
    ///     address, so clients that send no Id are still told apart by machine.
    /// </remarks>
    public static string GetClientKey(string? id, EndPoint? source = null)
    {
        if (!string.IsNullOrWhiteSpace(id)) return id;
        return GetSourceAddress(source) ?? UnknownClient;
    }

    /// <summary>
    ///     IP address of a UDP source endpoint, port excluded. A WSJT-X instance picks a fresh
    ///     ephemeral source port on every start, so including the port would turn one machine
    ///     into a new session on each restart.
    /// </summary>
    public static string? GetSourceAddress(EndPoint? source)
    {
        return source is IPEndPoint ip ? ip.Address.ToString() : null;
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
