using System;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.Models;

/// <summary>
///     Live state of a single WSJT-X / JTDX UDP client, keyed by its full UDP `Id`.
///     Acts as the single source of truth for "which clients exist and what are they doing".
/// </summary>
public class ClientSession : ReactiveObject
{
    public ClientSession(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    /// <summary>
    ///     Full client id from the UDP message. Unique key of the registry.
    /// </summary>
    public string Id { get; }

    /// <summary>
    ///     Short, human friendly label derived from <see cref="Id" />.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    ///     False once the client stops sending messages (expired) or sends a Close message.
    /// </summary>
    [Reactive] public bool IsAlive { get; set; }

    /// <summary>
    ///     UTC time the last message was received from this client.
    /// </summary>
    [Reactive] public DateTime LastSeenUtc { get; set; }

    /// <summary>
    ///     IP address the last message from this client arrived from. Same value the fallback
    ///     client key is derived from; null when the registry was fed without provenance.
    /// </summary>
    [Reactive] public string? SourceAddress { get; set; }

    [Reactive] public ulong DialFrequencyInHz { get; set; }
    [Reactive] public string Band { get; set; } = string.Empty;
    [Reactive] public string Mode { get; set; } = string.Empty;
    [Reactive] public string DECall { get; set; } = string.Empty;
    [Reactive] public string DEGrid { get; set; } = string.Empty;
    [Reactive] public string DXCall { get; set; } = string.Empty;
    [Reactive] public string DXGrid { get; set; } = string.Empty;
    [Reactive] public bool TxEnabled { get; set; }
    [Reactive] public bool Transmitting { get; set; }
    [Reactive] public bool Decoding { get; set; }
    [Reactive] public string ConfigurationName { get; set; } = string.Empty;
    [Reactive] public string TXMessage { get; set; } = string.Empty;

    /// <summary>
    ///     Number of decode messages received from this client.
    /// </summary>
    [Reactive] public long DecodedCount { get; set; }

    /// <summary>
    ///     Number of logged QSOs received from this client.
    /// </summary>
    [Reactive] public long QsosLoggedCount { get; set; }
}
