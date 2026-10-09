using System;
using System.Collections.Generic;
using CloudlogHelper.Models;
using DynamicData;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services.Interfaces;

/// <summary>
///     Single source of truth for the WSJT-X / JTDX clients currently talking to us,
///     keyed by the full UDP Id. Feed every incoming message via <see cref="Touch" />.
/// </summary>
public interface IClientRegistryService
{
    /// <summary>
    ///     Observable change set over the known clients, for binding in the UI.
    /// </summary>
    IObservable<IChangeSet<ClientSession, string>> Connect();

    /// <summary>
    ///     Snapshot of the currently known clients.
    /// </summary>
    IEnumerable<ClientSession> Sessions { get; }

    /// <summary>
    ///     Look up a client by id (empty ids map to the unknown bucket).
    /// </summary>
    ClientSession? GetSession(string id);

    /// <summary>
    ///     Record that a message was received from a client. Creates the session on first
    ///     sight and refreshes its liveness/bookkeeping on every call.
    /// </summary>
    void Touch(WsjtxMessage message);
}
