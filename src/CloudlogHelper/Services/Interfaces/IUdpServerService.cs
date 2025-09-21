using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services.Interfaces;

public interface IUdpServerService
{
    bool IsUdpServerRunning();

    Task ForwardUDPMessageAsync(Memory<byte> message, IPEndPoint endPoint);
    Task ForwardTCPMessageAsync(Memory<byte> message, string server);

    Task RestartUDPServerAsync(IPAddress ip, int port,
        Action<WsjtxMessage> handler,
        Action<Memory<byte>> rawhandler,
        Action<LogLevel, string>? udpLogger = null);

    Task TerminateUDPServerAsync();
}