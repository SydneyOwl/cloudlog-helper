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
    private CancellationTokenSource? _connectionCts;
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

        MessageBus.Current.Listen<SettingsChanged>().Subscribe((changed =>
        {
            if (changed.Part != ChangedPart.CLHServer)return;
            _ = ReconnectAsync();
        }));
        
        if (_appSettingsService.GetCurrentSettings().CLHServerSettings.IsEnabled)
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
            ClassLogger.Debug("Reconnect already in progress");
            return;
        }

        try
        {
            // Disconnect any existing connection first
            await DisconnectAsync();

            // Start background connection loop (with retries)
            _ = Task.Run(ConnectionLoop).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    ClassLogger.Error(t.Exception, "ConnectionLoop crashed unexpectedly");
            }, TaskScheduler.Default);
            ;
        }
        finally
        {
            await Task.Delay(2000);
            _reconnectSemaphore.Release();
        }
        
    }

    private async Task ConnectionLoop()
    {
        while (!_disposed)
        {
            var currentSettings = _appSettingsService.GetCurrentSettings();

            try
            {
                var cts = new CancellationTokenSource();
                var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                lock (_lock)
                {
                    if (_disposed) break;
                    _connectionCts = cts;
                }

                // Connect TCP
                var tcpClient = new TcpClient();
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));

                await tcpClient.ConnectAsync(currentSettings.CLHServerSettings.ServerHost,
                    currentSettings.CLHServerSettings.ServerPort, connectCts.Token);

                Stream stream = tcpClient.GetStream();

                // Upgrade to TLS if needed
                if (currentSettings.CLHServerSettings.UseTLS)
                {
                    // fixme: we do not check server certs for now
                    var sslStream = new SslStream(stream, leaveInnerStreamOpen: false,
                        userCertificateValidationCallback: (sender, cert, chain, errs) => true);

                    await sslStream.AuthenticateAsClientAsync(
                        currentSettings.CLHServerSettings.ServerHost,
                        clientCertificates: null,
                        enabledSslProtocols: SslProtocols.Tls12,
                        checkCertificateRevocation: false);

                    stream = sslStream;
                }

                // Commit resources only after successful connect
                lock (_lock)
                {
                    if (_disposed || cts.IsCancellationRequested)
                    {
                        tcpClient.Dispose();
                        stream.Dispose();
                        return;
                    }

                    _tcpClient = tcpClient;
                    _networkStream = stream;
                }

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // now we do login operation here
                await CLHServerUtil.WriteMsgAsync(_networkStream, new HandshakeRequest
                {
                    Os = RuntimeInformation.OSDescription,
                    Ver = VersionInfo.Version,
                    ClientType = "sender",
                    AuthKey = CLHServerUtil.CalcAuthKey(currentSettings.CLHServerSettings.ServerKey, now),
                    Timestamp = now,
                    RunId = currentSettings.InstanceName
                }, connectCts.Token);

                var handshakeResp = await CLHServerUtil.ReadMsgAsync(_networkStream, connectCts.Token);
                var con = (HandshakeResponse)handshakeResp;
                if (!(con).Accept)
                {
                    throw new Exception("Failed to do login: " + con.Error);
                }

                ClassLogger.Debug("Login successfully.");
                
                _onReceiveCallback += _heartbeatAckHandler;

                // recv
                _receiveTask = Task.Run(() => ReceiveLoop(cts.Token), cts.Token);

                // hb
                _ = Task.Run(() => SendHeartbeat(cts.Token), cts.Token);

                // check hb
                _ = Task.Run(() => ScanHeartbeatTimeout(cts.Token), cts.Token);

                ConnectionChanged?.Invoke(true);

                // Wait until disconnect or cancellation
                await _receiveTask;

                // if it was canceled manually - we just break
                if (cts.IsCancellationRequested)
                {
                    ClassLogger.Trace("Connection loop exited.");
                    break;
                }
            }
            catch (Exception ex) when (!_disposed)
            {
                ClassLogger.Error(ex, "Error occurred! ", ex);

                ConnectionChanged?.Invoke(false);

                // Clean up any leftovers
                CleanupConnectionResources();
            }
            finally
            {
                // Delay before retry — but respect dispose during wait
                await Task.Delay(DefaultConfigs.CLHTCPConnRetryDelayMs, CancellationToken.None);
            }
        }
    }

    private async Task SendHeartbeat(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (token.IsCancellationRequested)
                {
                    ClassLogger.Trace("SendHeartbeat loop exited.");
                    return;
                }

                var tm = DateTimeOffset.Now.ToUnixTimeSeconds();
            
                await SendData(new Ping
                {
                    Timestamp = tm
                });
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

    private async Task ScanHeartbeatTimeout(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.Now.ToUnixTimeSeconds() - _lastHeartbeatAckTimestamp >
                    DefaultConfigs.CLHHeartbeatTimeoutS)
                {
                    ClassLogger.Warn("Heartbeart Timeout! Reconnecting");
                    await ReconnectAsync();
                }
            }
            catch (Exception ex)
            {
                ClassLogger.Error("Unable to scan heartbeat: " + ex.Message);
            }
            finally
            {
                await Task.Delay(2000, CancellationToken.None);
            }
        }
        
        ClassLogger.Trace("ScanHeartbeatTimeout loop exited.");
    }

    private void _heartbeatAckHandler(IMessage message)
    {
        var messageDescriptor = message.Descriptor;
        if (message is Pong pong)
        {
            if (pong.Ack)
            {
                ClassLogger.Trace("Ping accepted !");
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
            var iMessage = await CLHServerUtil.ReadMsgAsync(stream, cancellationToken);
            _onReceiveCallback?.Invoke(iMessage);
        }
    }


    public async Task SendDataNoException(IMessage data)
    {
        try
        {
            await SendData(data);
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
        if (_disposed) throw new ObjectDisposedException(nameof(CLHServerService));
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (_networkStream is null || !_networkStream.CanWrite)
        {
            ClassLogger.Trace("Skip writing to stream due to unavailable connection");
            return;
        };
        await _sendLock.WaitAsync();

        try
        {
            await CLHServerUtil.WriteMsgAsync(_networkStream, data);
            await _networkStream.FlushAsync();
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

        CancellationTokenSource? ctsToCancel = null;
        lock (_lock)
        {
            ctsToCancel = _connectionCts;
            _connectionCts = null;
        }

        ctsToCancel?.Cancel();

        // Wait briefly for receive loop to exit
        if (_receiveTask != null)
        {
            try { await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { /* ignore */ }
        }

        CleanupConnectionResources();
        ConnectionChanged?.Invoke(false);
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

            _connectionCts?.Dispose();
            _connectionCts = null;
        }
    }

    // --- IDisposable ---

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // Stop all activity
        _connectionCts?.Cancel();
        CleanupConnectionResources();

        GC.SuppressFinalize(this);
    }
}