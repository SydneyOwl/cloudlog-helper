using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NLog;
using WsjtxUtilsPatch.WsjtxMessages;
using WsjtxUtilsPatch.WsjtxMessages.Messages;
using WsjtxUtilsPatch.WsjtxUdpServer;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CloudlogHelper.Services;

public class UdpServerService : IUdpServerService, IDisposable
{
    private readonly object _syncLock = new();

    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private bool _closing;

    private CancellationTokenSource _cts = new();

    private IPEndPoint _currentEndpoint;

    private UdpClient? _forwardedClient;
    private WsjtxUdpServer? _udpServer;

    public void Dispose()
    {
        _udpServer?.Dispose();
        _forwardedClient?.Dispose();
        _cts.Dispose();
    }

    public bool IsUdpServerRunning()
    {
        if (_udpServer is null) return false;
        return _udpServer.IsRunning;
    }

    public async Task ForwardUDPMessageAsync(Memory<byte> message, IPEndPoint endPoint)
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
            await _forwardedClient.SendAsync(message, endPoint, new CancellationTokenSource(
                TimeSpan.FromSeconds(DefaultConfigs.DefaultForwardingRequestTimeout)).Token);
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Failed to send message.");
            throw;
        }
    }

    public async Task ForwardTCPMessageAsync(Memory<byte> message, string server)
    {
        var deserializeWsjtxMessage = message.DeserializeWsjtxMessage();
        if (deserializeWsjtxMessage is null)
        {
            ClassLogger.Debug("deserializeWsjtxMessage is empty so skipped,,");
            return;
        }

        var receiveString = await server.WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithHeader("Content-Type", "application/json")
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultForwardingRequestTimeout))
            .PostStringAsync(JsonConvert.SerializeObject(deserializeWsjtxMessage))
            .ReceiveString();

        if (receiveString != "OK")
            throw new Exception($"Result does not return as expected: expect \"OK\" but we got {receiveString}");
    }

    public async Task RestartUDPServerAsync(IPAddress ip,
        int port, Action<WsjtxMessage> handler,
        Action<Memory<byte>> rawhandler,
        Action<LogLevel, string>? udpLogger = null)
    {
        try
        {
            await TerminateUDPServerAsync();
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


    public Task TerminateUDPServerAsync()
    {
        if (_udpServer is null) return Task.CompletedTask;
        if (_closing) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                _closing = true;
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
            finally
            {
                _closing = false;
            }

            _udpServer = null;
        });
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
            // ClassLogger.Log(NLog.LogLevel.FromOrdinal((int)logLevel), msg);
            _logIt?.Invoke(logLevel, msg);
        }
    }
}