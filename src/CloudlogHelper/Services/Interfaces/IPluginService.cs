using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudlogHelper.Services.Interfaces;

public interface IPluginService
{
    Task<IReadOnlyList<PluginInfo>> GetConnectedPluginsAsync();
    Task<bool> DisconnectPluginAsync(string pluginUuid, string reason, CancellationToken token, bool notify);
}