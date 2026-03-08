using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using Google.Protobuf;
using SydneyOwl.CLHProto.Plugin;

namespace CloudlogHelper.Services.Interfaces;

public interface IPluginService
{
    Task BroadcastMessageAsync(IMessage? message, CancellationToken token);
    Task<IReadOnlyList<PluginInfo>> GetConnectedPluginsAsync();
    Task<PipeServerInfo> GetServerInfoAsync(CancellationToken token);
    Task<PipeRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken token);
    Task<bool> DisconnectPluginAsync(string pluginUuid, string reason, CancellationToken token);
}