using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NLog;
using WsjtxUtils.WsjtxMessages.Messages;
using WsjtxUtils.WsjtxUdpServer;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CloudlogHelper.Utils;

public class UDPServerUtil
{
    private static WsjtxUdpServer? _udpServer;
    private static CancellationTokenSource _cts = new();

    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public static bool IsUdpServerRunning()
    {
        if (_udpServer is null) return false;
        return _udpServer.IsRunning;
    }

    public static async Task RestartUDPServerAsync(IPAddress ip, int port,
        Action<WsjtxMessage> handler,
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
                DefaultUDPMessageHandler.GenerateDefaultUDPMessageHandlerWithCallback(handler),
                ip,
                port,
                logger: new UDPServerLogger(udpLogger));
            _udpServer.Start(_cts);
        }
        catch (Exception e)
        {
            ClassLogger.Error($"Exception here: {e.Message}");
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
        }
        catch (Exception e)
        {
            ClassLogger.Warn($"Error occurred while shutting down udp server... {e.Message}");
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