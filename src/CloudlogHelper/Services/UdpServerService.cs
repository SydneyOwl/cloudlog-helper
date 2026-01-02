using System;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

    public async Task InitializeAsync(Action<WsjtxMessage> handler, Action<LogLevel, string> logger)
    {
        await _restartUDPServerAsync(handler, _rawHandler, logger);
        
        MessageBus.Current.Listen<SettingsChanged>()
            .Where(x => x.Part == ChangedPart.UDPServer)
            .Throttle(TimeSpan.FromMilliseconds(100)) // 防抖
            .Subscribe(_ =>
            {
                ClassLogger.Info("UDP settings changed!");
                _restartUDPServerAsync(handler, _rawHandler, logger);
            });
    }

    private async void _rawHandler(Memory<byte> message)
    {
        // raw hander do forward works
        try
        {
            var _udpSettings = _applicationSettingsService.GetCurrentSettings().UDPSettings;
            if (_udpSettings.ForwardMessage)
            {
                await  _forwardUDPMessageAsync(
                    message, 
                    IPEndPoint.Parse(_udpSettings.ForwardAddress));
            }

            if (_udpSettings.ForwardMessageToHttp)
            {
                await _forwardTCPMessageAsync(
                    message, 
                    _udpSettings.ForwardHttpAddress);
            }
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Failed to forward WSJT-X message");
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

    private async Task _forwardTCPMessageAsync(Memory<byte> message, string server)
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

    private async Task _restartUDPServerAsync(Action<WsjtxMessage> handler,
        Action<Memory<byte>> rawhandler,
        Action<LogLevel, string>? udpLogger = null)
    {
        try
        {
            await _terminateUDPServerAsync();

            if (!_applicationSettingsService.GetCurrentSettings().UDPSettings.EnableUDPServer)
            {
                return;
            }
            
            _cts = new CancellationTokenSource();
            // Small delay to ensure OS releases resources
            await Task.Delay(500);
            ClassLogger.Debug("Asking udpserver to start.");
            _udpServer = new WsjtxUdpServer(
                DefaultUDPMessageHandler.GenerateDefaultUDPMessageHandlerWithCallback(handler, rawhandler),
                _applicationSettingsService.GetCurrentSettings().UDPSettings.EnableConnectionFromOutside ? IPAddress.Any : IPAddress.Loopback,
                int.Parse(_applicationSettingsService.GetCurrentSettings().UDPSettings.UDPPort),
                logger: new UDPServerLogger(udpLogger));
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

            var msg = formatter(state, exception);
            // ClassLogger.Log(NLog.LogLevel.FromOrdinal((int)logLevel), msg);
            _logIt?.Invoke(logLevel, msg);
        }
    }
}