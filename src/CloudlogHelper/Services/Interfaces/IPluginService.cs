using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using SydneyOwl.CLHProto.Plugin;

namespace CloudlogHelper.Services.Interfaces;

public interface IPluginService
{
    Task InitPluginServicesAsync(CancellationToken token);
    
    Task BroadcastMessageAsync(IMessage? message, CancellationToken token);
}