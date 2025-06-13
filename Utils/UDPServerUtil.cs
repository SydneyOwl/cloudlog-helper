using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NLog;
using WsjtxUtilsPatch.WsjtxMessages.Messages;
using WsjtxUtilsPatch.WsjtxUdpServer;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CloudlogHelper.Utils;

public class UDPServerUtil
{
    private static WsjtxUdpServer? _udpServer;

    private static UdpClient? _forwardedClient;

    private static CancellationTokenSource _cts = new();

    private static IPEndPoint _currentEndpoint;

    private static readonly object _syncLock = new();

    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public static bool IsUdpServerRunning()
    {
        if (_udpServer is null) return false;
        return _udpServer.IsRunning;
    }

    public static async Task ForwardMessageAsync(Memory<byte> message, IPEndPoint endPoint)
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


    public static async Task RestartUDPServerAsync(IPAddress ip, int port,
        Action<WsjtxMessage> handler,
        Action<Memory<byte>> rawhandler,
        Action<LogLevel, string>? udpLogger = null)
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
            udpLogger?.Invoke(LogLevel.Error, e.Message);
        }
    }

    public static void TerminateUDPServer()
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
        private readonly Action<LogLevel, string>? _logIt;

        public UDPServerLogger(Action<LogLevel, string>? callback)
        {
            _logIt = callback;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return default!;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var msg = formatter(state, exception);
            ClassLogger.Log(NLog.LogLevel.FromOrdinal((int)logLevel), msg);
            _logIt?.Invoke(logLevel, msg);
        }
    }
}