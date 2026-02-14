using System.Threading;
using System.Threading.Tasks;

namespace CloudlogHelper.Services.Interfaces;

public interface IPluginService
{
    Task InitPluginServicesAsync(CancellationToken token);
}