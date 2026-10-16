using System;
using System.Collections.Generic;
using System.Net;
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
    ///     Look up a client by id. An empty id resolves to the source-address fallback when
    ///     <paramref name="source" /> is given, so fallback sessions stay findable.
    /// </summary>
    ClientSession? GetSession(string id, EndPoint? source = null);

    /// <summary>
    ///     Record that a message was received from a client. Creates the session on first
    ///     sight and refreshes its liveness/bookkeeping on every call.
    /// </summary>
    void Touch(WsjtxMessage message, EndPoint? source = null);
}
