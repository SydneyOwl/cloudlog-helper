using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using Google.Protobuf;

namespace CloudlogHelper.Services.Interfaces;

public interface ICLHServerService
{
    Task ReconnectAsync();
    Task SendData(IMessage data);
    Task SendDataNoException(IMessage data);
    Task DisconnectAsync();
    Task TestConnectionAsync(ApplicationSettings draftSetting, bool useTestMode = false);
}