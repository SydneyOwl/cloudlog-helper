using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using NLog;
using ReactiveUI;
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

    private IApplicationSettingsService _applicationSettingsService;
    public UdpServerService(IApplicationSettingsService applicationSettingsService)
    {
        _applicationSettingsService = applicationSettingsService;
    }

    public void Dispose()
    {
        _udpServer?.Dispose();
        _forwardedClient?.Dispose();
        _cts.Dispose();
    }

    public string GetUdpBindingAddress()
    {
        try
        {
            var settings = _applicationSettingsService.GetCurrentSettings().UDPSettings;
            var port = settings.UDPPort;
            if (string.IsNullOrEmpty(port))
            {
                return "(?)";
            }

            if (settings.EnableConnectionFromOutside)
            {
                return $"(0.0.0.0:{port})";
            }

            return $"(127.0.0.1:{port})";
        }
        catch (Exception a)
        {
            ClassLogger.Error(a,"Failed to update udp listening address.");
            return "?";
            // _windowNotificationManagerService.SendErrorNotificationSync(a.Message);
        }
    }

    public bool IsUdpServerEnabled()
    {
        return _applicationSettingsService.GetCurrentSettings().UDPSettings.EnableUDPServer;
    }

    public bool IsUdpServerRunning()
    {
        if (_udpServer is null) return false;
        return _udpServer.IsRunning;
    }

    public bool IsNotifyOnQsoMade()
    {
        return _applicationSettingsService.GetCurrentSettings().UDPSettings.PushNotificationOnQSOMade;
    }

    public bool IsNotifyOnQsoUploaded()
    {
        return _applicationSettingsService.GetCurrentSettings().UDPSettings.PushNotificationOnQSOUploaded;
    }

    public int QSOUploadRetryCount()
    {
        return int.TryParse(_applicationSettingsService.GetCurrentSettings().UDPSettings.RetryCount, out var ret) ? ret : 1;
    }

    private Func<WsjtxMessage, Task> _handlerWrapper(Func<WsjtxMessage, Task> handler)
    {
        return Wrapped;

        async Task Wrapped(WsjtxMessage msg)
        {
            try
            {
                await handler(msg).ConfigureAwait(false);
        
                var udpSettings = _applicationSettingsService.GetCurrentSettings().UDPSettings;
                if (udpSettings.ForwardMessageToHttp)
                {
                    await _forwardTCPMessageAsync(
                        msg,
                        udpSettings.ForwardHttpAddress).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ClassLogger.Debug(ex, "Error while handling message. ignored.");
            }
        }
    }

    public async Task InitializeAsync(Func<WsjtxMessage, Task> handler, Action<LogLevel, string> logger)
    {
        await _restartUDPServerAsync(_handlerWrapper(handler), _rawHandler, logger);
        
        MessageBus.Current.Listen<SettingsChanged>()
            .Where(x => x.Part == ChangedPart.UDPServer)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .Subscribe(_ =>
            {
                ClassLogger.Trace("UDP settings changed!");
                _restartUDPServerAsync(_handlerWrapper(handler), _rawHandler, logger);
            });
    }

    private async Task _rawHandler(Memory<byte> message)
    {
        try
        {
            // raw hander do forward works
            var _udpSettings = _applicationSettingsService.GetCurrentSettings().UDPSettings;
            if (_udpSettings.ForwardMessage)
            {
                await _forwardUDPMessageAsync(
                    message,
                    IPEndPoint.Parse(_udpSettings.ForwardAddress)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Debug(ex, "Error while forwarding message. ignored.");
        }
    }

    private async Task _forwardUDPMessageAsync(Memory<byte> message, IPEndPoint endPoint)
    {
        lock (_syncLock)
        {
            if (_forwardedClient == null || !Equals(_currentEndpoint, endPoint))
            {
                _forwardedClient?.Dispose();
                _forwardedClient = new UdpClient();
                _currentEndpoint = endPoint;
                ClassLogger.Trace("Created new UdpClient instance");
            }
            else
            {
                ClassLogger.Trace("Reusing client");
            }
        }

        await _forwardedClient.SendAsync(message, endPoint, new CancellationTokenSource(
            TimeSpan.FromSeconds(DefaultConfigs.DefaultForwardingRequestTimeout)).Token).ConfigureAwait(false);
    }

    private async Task _forwardTCPMessageAsync(WsjtxMessage message, string server)
    {
        var receiveString = await server.WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithHeader("Content-Type", "application/json")
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultForwardingRequestTimeout))
            .PostStringAsync(WsjtxMessageUtil.SerializeWsjtxMessageToJson(message))
            .ReceiveString().ConfigureAwait(false);

        if (receiveString != "OK")
            throw new Exception($"Result does not return as expected: expect \"OK\" but we got {receiveString}");
    }

    private async Task _restartUDPServerAsync(Func<WsjtxMessage, Task> handler,
        Func<Memory<byte>, Task> rawhandler,
        Action<LogLevel, string>? udpLogger = null)
    {
        try
        {
            await _terminateUDPServerAsync();

            if (!_applicationSettingsService.GetCurrentSettings().UDPSettings.EnableUDPServer)
            {
                return;
            }
            
            // Small delay to ensure OS releases resources
            await Task.Delay(500);
            
            _cts = new CancellationTokenSource();
            ClassLogger.Trace("Asking udpserver to start.");

            var wrappedLogger = new UDPServerLogger(udpLogger);

            _udpServer = new WsjtxUdpServer(
                DefaultUDPMessageHandler.GenerateDefaultUDPMessageHandlerWithCallback(handler, rawhandler),
                _applicationSettingsService.GetCurrentSettings().UDPSettings.EnableConnectionFromOutside ? IPAddress.Any : IPAddress.Loopback,
                int.Parse(_applicationSettingsService.GetCurrentSettings().UDPSettings.UDPPort),
                logger: wrappedLogger);
            _udpServer.Start(_cts);
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Error starting udp.");
            udpLogger?.Invoke(LogLevel.Error, e.Message);
        }
    }


    private Task _terminateUDPServerAsync()
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
            if (exception is null) return;

            // var msg = formatter(state, exception);
            // ClassLogger.Log(NLog.LogLevel.FromOrdinal((int)logLevel), msg);
            _logIt?.Invoke(logLevel, exception.Message);
        }
    }
}