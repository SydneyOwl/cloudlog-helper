using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.CLHProto;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Google.Protobuf;
using NLog;
using ReactiveUI;

public class CLHServerService : ICLHServerService, IDisposable
{
    private readonly IApplicationSettingsService _appSettingsService; // Needed to monitor that in realtime...
    
    private TcpClient? _tcpClient;
    private Stream? _networkStream;
    
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private Task? _connectionLoopTask;
    
    private CancellationTokenSource? _connectionSubtaskCts;
    private CancellationTokenSource? _connectionLoopCts;
    private readonly object _lock = new object();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _reconnectSemaphore = new(1, 1);
    private bool _disposed = false;
    private bool _isLocked;
    private event Action<IMessage> _onReceiveCallback;
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public event Action<bool>? ConnectionChanged;
    
    private long _lastHeartbeatAckTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
    

    public CLHServerService(IApplicationSettingsService? appSettingsService)
    {
        _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));

        MessageBus.Current.Listen<SettingsChanged>().Subscribe((async void (changed) =>
        {
            if (changed.Part != ChangedPart.CLHServer)return;
            await ReconnectAsync().ConfigureAwait(false);
        }));
        
        if (_appSettingsService.GetCurrentSettings().CLHServerSettings.CLHServerEnabled)
        {
            _ = ReconnectAsync();
        }
    }
    

    /// <summary>
    /// Starts or restarts connection using current settings.
    /// Safe to call multiple times (e.g., after config change).
    /// </summary>
    public async Task ReconnectAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CLHServerService));
        
        if (!await _reconnectSemaphore.WaitAsync(0))  // 非阻塞尝试
        {
            ClassLogger.Warn("Reconnect already in progress");
            return;
        }

        try
        {
            // Disconnect any existing connection first
            await DisconnectAsync().ConfigureAwait(false);

            if (!_appSettingsService.GetCurrentSettings().CLHServerSettings.CLHServerEnabled)
            {
                // ClassLogger.Warn("Okay! Now we dont connect to clhserver!!!");
                return;
            }

            _connectionLoopCts = new CancellationTokenSource();

            // Start background connection loop (with retries)
            _connectionLoopTask = Task.Run(()=>ConnectionLoop(_connectionLoopCts.Token));
        }
        finally
        {
            await Task.Delay(2000);
            _reconnectSemaphore.Release();
        }
    }

    private async Task ConnectionLoop(CancellationToken mainToken)
    {
        while (!_disposed && !mainToken.IsCancellationRequested)
        {
            try
            {
                var currentSettings = _appSettingsService.GetCurrentSettings();
                var cts = new CancellationTokenSource();
                var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                lock (_lock)
                {
                    if (_disposed) throw new ObjectDisposedException("Service already disposed");
                    _connectionSubtaskCts = cts;
                }
                
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                
                await _connectAndLogin(currentSettings.CLHServerSettings.ServerHost,
                    currentSettings.CLHServerSettings.ServerPort,
                    currentSettings.CLHServerSettings.UseTLS, 
                    currentSettings.CLHServerSettings.ServerKey,
                    currentSettings.InstanceName,
                    cts.Token, connectCts.Token).ConfigureAwait(false);
                
                _onReceiveCallback += _heartbeatAckHandler;

                // recv
                _receiveTask = Task.Run(() => ReceiveLoop(cts.Token), cts.Token);

                // hb
                _heartbeatTask = Task.Run(() => SendHeartbeat(cts.Token), cts.Token);

                // check hb
                // _ = Task.Run(() => ScanHeartbeatTimeout(cts.Token), cts.Token);

                ConnectionChanged?.Invoke(true);

                // Wait until disconnect or cancellation
                await _receiveTask;
            }
            catch (Exception ex) when (!_disposed)
            {
                ClassLogger.Error(ex, "Error occurred in connection loop. ");
            }
            finally
            {
                ConnectionChanged?.Invoke(false);
                // Clean up any leftovers
                CleanupConnectionResources();
                // Delay before retry — but respect dispose during wait
                await Task.Delay(DefaultConfigs.CLHTCPConnRetryDelayMs, CancellationToken.None);
            }
        }
    }

    private async Task _connectAndLogin(string host, int port, bool tls, 
        string key, string instanceName,
        CancellationToken globalCtx, CancellationToken connectCts, bool useTestMode = false)
    {
        // Connect TCP
        var tcpClient = new TcpClient();

        await tcpClient.ConnectAsync(host, port, connectCts).ConfigureAwait(false);

        Stream stream = tcpClient.GetStream();

        // Upgrade to TLS if needed
        if (tls)
        {
            // fixme: we do not check server certs for now
            var sslStream = new SslStream(stream, leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (sender, cert, chain, errs) => true);

            await sslStream.AuthenticateAsClientAsync(
                host,
                clientCertificates: null,
                enabledSslProtocols: SslProtocols.Tls12,
                checkCertificateRevocation: false).ConfigureAwait(false);

            stream = sslStream;
        }

        // Commit resources only after successful connect
        lock (_lock)
        {
            if (_disposed || globalCtx.IsCancellationRequested)
            {
                tcpClient.Dispose();
                stream.Dispose();
                return;
            }

            if (!useTestMode)
            {
                _tcpClient = tcpClient;
                _networkStream = stream;
            }
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // now we do login operation here
        await CLHServerUtil.WriteMsgAsync(stream, new HandshakeRequest
        {
            Os = RuntimeInformation.OSDescription,
            Ver = VersionInfo.Version,
            ClientType = useTestMode ? "connTest" : "sender",
            AuthKey = CLHServerUtil.CalcAuthKey(key, now),
            Timestamp = now,
            RunId = $"{instanceName}{(useTestMode ? "(test)" : "")}"
        }, connectCts).ConfigureAwait(false);

        var handshakeResp = await CLHServerUtil.ReadMsgAsync(stream, connectCts).ConfigureAwait(false);
        var con = (HandshakeResponse)handshakeResp;
        if (!(con).Accept)
        {
            throw new Exception("Failed to do login: " + con.Error);
        }

        ClassLogger.Debug("Login successfully.");
    }

    private async Task SendHeartbeat(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var tm = DateTimeOffset.Now.ToUnixTimeSeconds();
            
                await SendData(new Ping
                {
                    Timestamp = tm
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ClassLogger.Error("Unable to send heartbeat: " + ex.Message);
            }
            finally
            {
                await Task.Delay(DefaultConfigs.CLHHeartbeatIntervalMS, CancellationToken.None);
            }
        }
        ClassLogger.Trace("Send ping loop exited.");
    }

    private void _heartbeatAckHandler(IMessage message)
    {
        var messageDescriptor = message.Descriptor;
        if (message is Pong pong)
        {
            if (pong.Ack)
            {
                ClassLogger.Trace("Ping accepted.");
                lock (_lock)
                {
                    _lastHeartbeatAckTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                }
                return;
            }
            ClassLogger.Error($"Server decined our ping request({pong.Error})");
        }
    }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        var stream = _networkStream;
        if (stream == null) return;

        while (!cancellationToken.IsCancellationRequested && stream.CanRead)
        {
            var iMessage = await CLHServerUtil.ReadMsgAsync(stream, cancellationToken).ConfigureAwait(false);
            _onReceiveCallback?.Invoke(iMessage);
        }
    }


    public async Task SendDataNoException(IMessage data)
    {
        try
        {
            await SendData(data).ConfigureAwait(false);
        }catch(Exception ex)
        {
            ClassLogger.Error("Error sending data: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Sends message over the current connection.
    /// </summary>
    public async Task SendData(IMessage data)
    {           
        if (!_appSettingsService.GetCurrentSettings().CLHServerSettings.CLHServerEnabled) return;
        if (_disposed) throw new ObjectDisposedException(nameof(CLHServerService));
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (_networkStream is null)
        {
            ClassLogger.Trace("Skip writing to stream due to unavailable connection");
            return;
        };
        await _sendLock.WaitAsync();

        try
        {
            await CLHServerUtil.WriteMsgAsync(_networkStream, data).ConfigureAwait(false);
            await _networkStream.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Gracefully disconnects the current connection (if any).
    /// Does NOT dispose the service — reconnect is allowed.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_disposed) return;

        // CancellationTokenSource? ctsToCancel = null;
        // lock (_lock)
        // {
        //     ctsToCancel = _connectionCts;
        //     _connectionCts = null;
        // }
        
        _connectionSubtaskCts?.Cancel();
        _connectionLoopCts?.Cancel();

        // Wait briefly for receive loop to exit
    
        try
        {
            if (_receiveTask != null) await _receiveTask.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            ClassLogger.Debug("Receive loop exited.");
            if (_heartbeatTask != null) await _heartbeatTask.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            ClassLogger.Debug("Heartbeat exited.");
            if (_connectionLoopTask != null) await _connectionLoopTask.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            ClassLogger.Debug("Main exited.");
        }
        catch { /* ignore */ }

        CleanupConnectionResources();
        ConnectionChanged?.Invoke(false);
    }

    public async Task TestConnectionAsync(ApplicationSettings draftSetting, bool useTestMode = false)
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await _connectAndLogin(draftSetting.CLHServerSettings.ServerHost,
            draftSetting.CLHServerSettings.ServerPort,
            draftSetting.CLHServerSettings.UseTLS,
            draftSetting.CLHServerSettings.ServerKey,
            draftSetting.InstanceName,
            cts.Token, cts.Token,
            useTestMode).ConfigureAwait(false);
    }

    private void CleanupConnectionResources()
    {
        lock (_lock)
        {
            _onReceiveCallback -= _heartbeatAckHandler;
            // _receiveTask?.Dispose();
            _receiveTask = null;

            _networkStream?.Dispose();
            _networkStream = null;

            _tcpClient?.Dispose();
            _tcpClient = null;

            _connectionSubtaskCts?.Cancel();
            _connectionSubtaskCts?.Dispose();
            _connectionSubtaskCts = null;
        }
    }

    // --- IDisposable ---

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // Stop all activity
        CleanupConnectionResources();
        _connectionLoopCts?.Cancel();
        _connectionLoopCts?.Dispose();

        GC.SuppressFinalize(this);
    }
}