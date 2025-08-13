using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Microsoft.Extensions.Logging;
using NLog;
using WsjtxUtilsPatch.WsjtxMessages.Messages;
using WsjtxUtilsPatch.WsjtxUdpServer;
using LogLevel = NLog.LogLevel;

namespace CloudlogHelper.Services;

public class UdpServerService : IUdpServerService, IDisposable
{
    private WsjtxUdpServer? _udpServer;

    private UdpClient? _forwardedClient;

    private CancellationTokenSource _cts = new();

    private IPEndPoint _currentEndpoint;

    private readonly object _syncLock = new();

    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public bool IsUdpServerRunning()
    {
        if (_udpServer is null) return false;
        return _udpServer.IsRunning;
    }

    public async Task ForwardMessageAsync(Memory<byte> message, IPEndPoint endPoint)
    {
        lock (_syncLock)
        {
            if (_forwardedClient == null || !Equals(_currentEndpoint, endPoint))
            {
                _forwardedClient?.Dispose();
                _forwardedClient = new UdpClient();
                _currentEndpoint = endPoint;
                ClassLogger.Debug("Created new UdpClient instance");
            }
            else
            {
                ClassLogger.Debug("Reusing client");
            }
        }

        try
        {
            await _forwardedClient.SendAsync(message, endPoint);
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Failed to send message.");
            throw;
        }
    }

    public async Task RestartUDPServerAsync(IPAddress ip,
        int port, Action<WsjtxMessage> handler, 
        Action<Memory<byte>> rawhandler, 
        Action<Microsoft.Extensions.Logging.LogLevel, string>? udpLogger = null)
    {
        try
        {
            TerminateUDPServer();
            _cts = new CancellationTokenSource();
            // Small delay to ensure OS releases resources
            await Task.Delay(500);
            ClassLogger.Debug("Asking udpserver to start.");
            _udpServer = new WsjtxUdpServer(
                DefaultUDPMessageHandler.GenerateDefaultUDPMessageHandlerWithCallback(handler, rawhandler),
                ip,
                port,
                logger: new UDPServerLogger(udpLogger));
            _udpServer.Start(_cts);
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Error starting udp.");
            udpLogger?.Invoke(Microsoft.Extensions.Logging.LogLevel.Error, e.Message);
        }
    }
    

    public void TerminateUDPServer()
    {
        if (_udpServer is null) return;
        try
        {
            ClassLogger.Debug("Shutting down udp...");
            if (!_cts.IsCancellationRequested) _cts.Cancel();
            if (_udpServer.IsRunning) _udpServer?.Stop();
            if (!_udpServer!.IsDisposed) _udpServer?.Dispose();
            _forwardedClient?.Dispose();
            _forwardedClient = null;
            _currentEndpoint = null;
        }
        catch (Exception e)
        {
            ClassLogger.Warn(e, "Error occurred while shutting down udp server");
            // ignored...
        }

        _udpServer = null;
    }


    private sealed class UDPServerLogger : ILogger<WsjtxUdpServer>
    {
        private readonly Action<Microsoft.Extensions.Logging.LogLevel, string>? _logIt;

        public UDPServerLogger(Action<Microsoft.Extensions.Logging.LogLevel, string>? callback)
        {
            _logIt = callback;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return default!;
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var msg = formatter(state, exception);
            // ClassLogger.Log(NLog.LogLevel.FromOrdinal((int)logLevel), msg);
            _logIt?.Invoke(logLevel, msg);
        }
    }

    public void Dispose()
    {
        _udpServer?.Dispose();
        _forwardedClient?.Dispose();
        _cts.Dispose();
    }
}