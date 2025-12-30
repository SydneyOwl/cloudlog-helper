using System.Threading.Tasks;
using Google.Protobuf;

namespace CloudlogHelper.Services.Interfaces;

public interface ICLHServerService
{
    Task ReconnectAsync();
    Task SendData(IMessage data);
    Task SendDataNoException(IMessage data);
    Task DisconnectAsync();
}