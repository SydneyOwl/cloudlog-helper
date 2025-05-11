using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using WsjtxUtils.WsjtxMessages.Messages;
using WsjtxUtils.WsjtxUdpServer;

namespace CloudlogHelper.Utils;

public sealed class DefaultUDPMessageHandler : WsjtxUdpServerBaseAsyncMessageHandler
{
    private Action<WsjtxMessage>? _onMessageReceived;

    private DefaultUDPMessageHandler()
    {
    }

    public static DefaultUDPMessageHandler GenerateDefaultUDPMessageHandlerWithCallback(Action<WsjtxMessage> callback)
    {
        var defHandler = new DefaultUDPMessageHandler
        {
            _onMessageReceived = callback,
            ConnectedClientExpiryInSeconds = DefaultConfigs.UDPClientExpiryInSeconds
        };
        return defHandler;
    }

    #region IWsjtxUdpMessageHandler

    public override async Task HandleClearMessageAsync(WsjtxUdpServer server, Clear message, EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        _onMessageReceived?.Invoke(message);
        await base.HandleClearMessageAsync(server, message, endPoint, cancellationToken);
    }

    public override async Task HandleClosedMessageAsync(WsjtxUdpServer server, Close message, EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        _onMessageReceived?.Invoke(message);
        await base.HandleClosedMessageAsync(server, message, endPoint, cancellationToken);
    }

    public override async Task HandleDecodeMessageAsync(WsjtxUdpServer server, Decode message, EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        _onMessageReceived?.Invoke(message);
        await base.HandleDecodeMessageAsync(server, message, endPoint, cancellationToken);
    }

    public override async Task HandleHeartbeatMessageAsync(WsjtxUdpServer server, Heartbeat message, EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        _onMessageReceived?.Invoke(message);
        await base.HandleHeartbeatMessageAsync(server, message, endPoint, cancellationToken);
    }

    public override async Task HandleLoggedAdifMessageAsync(WsjtxUdpServer server, LoggedAdif message,
        EndPoint endPoint, CancellationToken cancellationToken = default)
    {
        _onMessageReceived?.Invoke(message);
        await base.HandleLoggedAdifMessageAsync(server, message, endPoint, cancellationToken);
    }

    public override async Task HandleQsoLoggedMessageAsync(WsjtxUdpServer server, QsoLogged message, EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        _onMessageReceived?.Invoke(message);
        await base.HandleQsoLoggedMessageAsync(server, message, endPoint, cancellationToken);
    }

    public override async Task HandleStatusMessageAsync(WsjtxUdpServer server, Status message, EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        _onMessageReceived?.Invoke(message);
        await base.HandleStatusMessageAsync(server, message, endPoint, cancellationToken);
    }

    public override async Task HandleWSPRDecodeMessageAsync(WsjtxUdpServer server, WSPRDecode message,
        EndPoint endPoint, CancellationToken cancellationToken = default)
    {
        _onMessageReceived?.Invoke(message);
        await base.HandleWSPRDecodeMessageAsync(server, message, endPoint, cancellationToken);
    }

    #endregion
}