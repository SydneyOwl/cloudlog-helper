using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using DynamicData.Binding;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Nito.AsyncEx;
using NLog;
using ReactiveUI;
using SydneyOwl.CLHProto.Plugin;

namespace CloudlogHelper.Services;


public class PluginInfo : IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly Func<PluginInfo, PipeControlRequest, CancellationToken, Task> _controlRequestHandler;
    private readonly Action<PluginInfo, PipePluginLog> _pluginLogHandler;
    private readonly object _subscriptionLock = new();
    private readonly Dictionary<string, string> _metadata = new();

    private NamedPipeServerStream? _client;
    private readonly SemaphoreSlim _cliLock = new(1, 1);

    private Task? _messageLoopTask;
    private CancellationTokenSource? _messageLoopCts;
    private bool _disposed;
    private long _lastHeartbeatTicks;

    private MessageType[]? _wsjtxMessageTypes;
    private DecodeDeliveryMode _decodeDeliveryMode = DecodeDeliveryMode.Batched;

    public string Uuid { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public Capability[] Capabilities { get; private set; } = Array.Empty<Capability>();
    public string Description { get; private set; } = string.Empty;
    public string SdkName { get; private set; } = string.Empty;
    public string SdkVersion { get; private set; } = string.Empty;
    public DateTime RegisteredAt { get; private set; }
    public DateTime LastHeartbeat => new(Interlocked.Read(ref _lastHeartbeatTicks), DateTimeKind.Utc);
    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    private PluginInfo(
        Func<PluginInfo, PipeControlRequest, CancellationToken, Task> controlRequestHandler,
        Action<PluginInfo, PipePluginLog> pluginLogHandler)
    {
        _controlRequestHandler = controlRequestHandler;
        _pluginLogHandler = pluginLogHandler;
    }
    
    private void UpdateHeartbeat()
    {
        Interlocked.Exchange(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);
    }

    private async Task ReceiveClientMessagesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? client;

            await _cliLock.WaitAsync(token);
            try
            {
                if (_client is null || !_client.IsConnected)
                    break;
                client = _client;
            }
            finally
            {
                _cliLock.Release();
            }

            try
            {
                var anyMessage = await Any.Parser.ParseDelimitedFromAsync(client, token);
                if (anyMessage is null)
                {
                    ClassLogger.Info($"{Name} disconnected from plugin pipe.");
                    break;
                }

                UpdateHeartbeat();

                if (anyMessage.Is(PipeHeartbeat.Descriptor))
                {
                    var heartbeat = anyMessage.Unpack<PipeHeartbeat>();
                    if (!string.IsNullOrWhiteSpace(heartbeat.Uuid) && heartbeat.Uuid != Uuid)
                    {
                        ClassLogger.Warn(
                            $"Heartbeat uuid mismatch for plugin {Name}. Expected {Uuid}, got {heartbeat.Uuid}");
                    }
                    continue;
                }

                if (anyMessage.Is(PipeControlRequest.Descriptor))
                {
                    var request = anyMessage.Unpack<PipeControlRequest>();
                    await _controlRequestHandler(this, request, token);
                    continue;
                }

                if (anyMessage.Is(PipePluginLog.Descriptor))
                {
                    var pluginLog = anyMessage.Unpack<PipePluginLog>();
                    _pluginLogHandler(this, pluginLog);
                    continue;
                }

                ClassLogger.Warn($"Unknown plugin message received from {Name}: {anyMessage.TypeUrl}");
            }
            catch (IOException) when (!token.IsCancellationRequested)
            {
                ClassLogger.Info($"{Name} exited due to IO exception");
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ClassLogger.Error(ex, $"Error receiving message from plugin {Name}");
                await Task.Delay(500, token);
            }
        }
    }

    private void StartMessageLoop(CancellationToken token)
    {
        if (_disposed) return;

        _cliLock.Wait(token);
        try
        {
            if (_messageLoopTask != null || _client == null) return;
            _messageLoopCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _messageLoopTask = Task.Run(() => ReceiveClientMessagesAsync(_messageLoopCts.Token), token);
        }
        finally
        {
            _cliLock.Release();
        }
    }

    private void StopAll()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            SendMessage(new PipeConnectionClosed
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // ignored
        }

        _messageLoopCts?.Cancel();
        _messageLoopCts?.Dispose();
        _messageLoopCts = null;

        _cliLock.Wait();
        try
        {
            if (_client != null)
            {
                try
                {
                    _client.Close();
                    _client.Dispose();
                    _client = null;
                }
                catch (Exception ex)
                {
                    ClassLogger.Error(ex, $"Error closing pipe for {Name}");
                }
            }
        }
        finally
        {
            _cliLock.Release();
        }
    }

    public static PluginInfo Create(
        PipeRegisterPluginReq rpcRegisterPluginReq,
        NamedPipeServerStream client,
        CancellationToken token,
        Func<PluginInfo, PipeControlRequest, CancellationToken, Task> controlRequestHandler,
        Action<PluginInfo, PipePluginLog> pluginLogHandler)
    {
        var pg = new PluginInfo(controlRequestHandler, pluginLogHandler)
        {
            Uuid = rpcRegisterPluginReq.Uuid,
            Name = rpcRegisterPluginReq.Name,
            Version = rpcRegisterPluginReq.Version,
            Capabilities = rpcRegisterPluginReq.Capabilities.ToArray(),
            Description = rpcRegisterPluginReq.Description,
            SdkName = rpcRegisterPluginReq.SdkName,
            SdkVersion = rpcRegisterPluginReq.SdkVersion,
            _client = client,
            RegisteredAt = DateTime.UtcNow
        };

        foreach (var (key, value) in rpcRegisterPluginReq.Metadata)
            pg._metadata[key] = value;

        pg.UpdateWsjtxSubscription(rpcRegisterPluginReq.WsjtxSubscription);
        pg.UpdateHeartbeat();
        pg.StartMessageLoop(token);
        return pg;
    }

    public bool HasCapability(Capability capability)
    {
        return Capabilities.Contains(capability);
    }

    public bool ShouldReceiveWsjtx(MessageType messageType)
    {
        if (!HasCapability(Capability.WsjtxMessage))
            return false;

        MessageType[]? filters;
        lock (_subscriptionLock)
        {
            filters = _wsjtxMessageTypes;
        }

        if (filters is null || filters.Length == 0)
            return true;

        return Array.IndexOf(filters, messageType) >= 0;
    }

    public bool WantsBatchedDecode()
    {
        if (!ShouldReceiveWsjtx(MessageType.Decode))
            return false;

        DecodeDeliveryMode mode;
        lock (_subscriptionLock)
        {
            mode = _decodeDeliveryMode;
        }

        return mode is DecodeDeliveryMode.Batched or DecodeDeliveryMode.Both;
    }

    public bool WantsRealtimeDecode()
    {
        if (!ShouldReceiveWsjtx(MessageType.Decode))
            return false;

        DecodeDeliveryMode mode;
        lock (_subscriptionLock)
        {
            mode = _decodeDeliveryMode;
        }

        return mode is DecodeDeliveryMode.Realtime or DecodeDeliveryMode.Both;
    }

    public void UpdateWsjtxSubscription(PipeWsjtxSubscription? subscription)
    {
        if (subscription is null) return;
            
        var delivery = subscription.DecodeDeliveryMode switch
        {
            DecodeDeliveryMode.Unspecified => DecodeDeliveryMode.Batched,
            DecodeDeliveryMode.Batched => DecodeDeliveryMode.Batched,
            DecodeDeliveryMode.Realtime => DecodeDeliveryMode.Realtime,
            DecodeDeliveryMode.Both => DecodeDeliveryMode.Both,
            _ => DecodeDeliveryMode.Batched
        };

        lock (_subscriptionLock)
        {
            _decodeDeliveryMode = delivery;
            _wsjtxMessageTypes =  subscription.MessageTypes.Distinct().ToArray();
        }
    }

    public PipeWsjtxSubscription GetWsjtxSubscriptionSnapshot()
    {
        var snapshot = new PipeWsjtxSubscription();
        lock (_subscriptionLock)
        {
            snapshot.DecodeDeliveryMode = _decodeDeliveryMode;
            if (_wsjtxMessageTypes != null)
                snapshot.MessageTypes.Add(_wsjtxMessageTypes);
        }

        return snapshot;
    }

    public PipePluginInfo ToPipePluginInfo()
    {
        var info = new PipePluginInfo
        {
            Uuid = Uuid,
            Name = Name,
            Version = Version,
            Description = Description,
            RegisteredAt = Timestamp.FromDateTime(RegisteredAt),
            LastHeartbeat = Timestamp.FromDateTime(LastHeartbeat),
            WsjtxSubscription = GetWsjtxSubscriptionSnapshot()
        };

        info.Capabilities.Add(Capabilities);
        foreach (var (key, value) in _metadata)
            info.Metadata[key] = value;

        return info;
    }

    public async Task SendMessage<T>(T msg, CancellationToken token) where T : IMessage
    {
        switch (msg)
        {
            case PackedDecodeMessage:
                if (!WantsBatchedDecode()) return;
                break;
            case WsjtxMessage wsjtxMessage:
                var messageType = wsjtxMessage.Header?.Type ?? MessageType.Heartbeat;
                if (!ShouldReceiveWsjtx(messageType)) return;
                if (wsjtxMessage.PayloadCase == WsjtxMessage.PayloadOneofCase.Decode && !WantsRealtimeDecode()) return;
                break;
            case RigData:
                if (!HasCapability(Capability.RigData)) return;
                break;
            case ClhInternalMessage:
                if (!HasCapability(Capability.ClhInternalData)) return;
                break;
            case PipeControlResponse:
            case PipeConnectionClosed:
                break;
            default:
                return;
        }

        try
        {
            var any = Any.Pack(msg);

            await _cliLock.WaitAsync(token);
            try
            {
                if (_client is null || !_client.IsConnected) return;
                await any.WriteDelimitedToAsync(_client, token);
                await _client.FlushAsync(token);
            }
            finally
            {
                _cliLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            ClassLogger.Debug(ex, $"Failed to send message to plugin {Name}");
        }
    }

    public void Dispose()
    {
        StopAll();
    }
}

public class PluginService : IPluginService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly List<PluginInfo> _plugins = new();
    private readonly AsyncReaderWriterLock _pluginLock = new();

    private CancellationTokenSource? _source;
    private Task? _pluginTask;
    private readonly object _serviceLock = new();
    private bool _isRunning;
    
    private readonly ObservableCollection<Decode> _wsjtxDecodeCache = new();
    
    private readonly BasicSettings _basicSettings;
    private readonly CompositeDisposable _settingsSubscription = new();
    
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObservableCollection<Decode>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<PluginInfo>))]
    public PluginService(IApplicationSettingsService service)
    {
        _basicSettings = service.GetCurrentSettings().BasicSettings;
        
        _settingsSubscription.Add(
            _wsjtxDecodeCache.ObserveCollectionChanges()
                .Throttle(TimeSpan.FromSeconds(2))
                .ObserveOn(RxApp.TaskpoolScheduler)
                .Subscribe(async changes => 
                {
                    try
                    {
                        await HandleDecodeCacheChanged();
                    }
                    catch (Exception ex)
                    {
                        ClassLogger.Error(ex, "Error handling decode cache changes");
                    }
                })
        );
        
        _settingsSubscription.Add(
            MessageBus.Current
                .Listen<SettingsChanged>()
                .Where(x => x.Part == ChangedPart.BasicSettings)
                .Subscribe(async _ =>
                {
                    await HandleSettingsChange();
                })); 
            
        if (_basicSettings.EnablePlugin)
        {
            _ = StartPluginServiceAsync();
        }
    }

    private async Task HandleDecodeCacheChanged()
    {
        if (!_isRunning) return;
        
        ClassLogger.Trace("Sending throttled decoded message.");
        var decodes = _wsjtxDecodeCache.ToArray();
        if (decodes.Length == 0) return;
        
        _wsjtxDecodeCache.Clear();
        var packedMessage = new PackedDecodeMessage();
        packedMessage.Messages.AddRange(decodes);
        packedMessage.Timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        await BroadcastMessageAsync(packedMessage, CancellationToken.None);
    }

    private async Task HandleSettingsChange()
    {
        try
        {
            if (_basicSettings.EnablePlugin && !_isRunning)
            {
                ClassLogger.Info("Plugin service enabled via settings change");
                await StartPluginServiceAsync();
            }
            else if (!_basicSettings.EnablePlugin && _isRunning)
            {
                ClassLogger.Info("Plugin service disabled via settings change");
                await StopPluginServiceAsync();
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Error handling plugin service settings change");
        }
    }

    private Task StartPluginServiceAsync()
    {
        lock (_serviceLock)
        {
            if (_isRunning) return Task.CompletedTask;
            
            try
            {
                _source = new CancellationTokenSource();
                var tk = _source.Token;
                _pluginTask = Task.Run(() => _startService(tk), tk);
                _isRunning = true;
                ClassLogger.Info("Plugin service started");
            }
            catch (Exception ex)
            {
                ClassLogger.Error(ex, "Failed to start plugin service");
                _source?.Dispose();
                _source = null;
            }
        }

        return Task.CompletedTask;
    }

    private async Task StopPluginServiceAsync()
    {
        List<PluginInfo> pluginsToDispose;
        
        lock (_serviceLock)
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            
            // Stop the main service
            _source?.Cancel();
            _source?.Dispose();
            _source = null;
        }
        
        // Wait for the plugin task to complete with timeout
        if (_pluginTask != null)
        {
            try
            {
                await _pluginTask.WaitAsync(TimeSpan.FromSeconds(5));
                _pluginTask.Dispose();
                _pluginTask = null;
            }
            catch (TimeoutException)
            {
                ClassLogger.Warn("Plugin service task did not stop gracefully within timeout");
            }
            catch (Exception ex)
            {
                ClassLogger.Error(ex, "Error stopping plugin service task");
            }
        }
        
        // Clean up all registered plugins
        using (var writerLock = await _pluginLock.WriterLockAsync(CancellationToken.None))
        {
            pluginsToDispose = new List<PluginInfo>(_plugins);
            _plugins.Clear();
        }
        
        // Dispose plugins outside the lock
        foreach (var plugin in pluginsToDispose)
        {
            try
            {
                plugin.Dispose();
            }
            catch (Exception ex)
            {
                ClassLogger.Error(ex, $"Error disposing plugin {plugin.Name}");
            }
        }
        
        // Clear decode cache
        _wsjtxDecodeCache.Clear();
        
        ClassLogger.Info("Plugin service stopped");
    }

    public void Dispose()
    {
        _settingsSubscription?.Dispose();
        
        if (_isRunning)
        {
            Task.Run(StopPluginServiceAsync).GetAwaiter().GetResult();
        }
    }

    public async Task BroadcastMessageAsync(IMessage? message, CancellationToken token)
    {
        if (message is null) return;
        
        if (!_isRunning) return;

        if (message is WsjtxMessage { PayloadCase: WsjtxMessage.PayloadOneofCase.Decode } wsjtxDecodeMessage)
        {
            var realtimeTasks = new List<Task>();
            var hasBatchedDecodeSubscriber = false;

            using (var readerLock = await _pluginLock.ReaderLockAsync(token))
            {
                foreach (var pluginInfo in _plugins)
                {
                    if (pluginInfo.WantsRealtimeDecode())
                        realtimeTasks.Add(pluginInfo.SendMessage(wsjtxDecodeMessage, token));

                    if (pluginInfo.WantsBatchedDecode())
                        hasBatchedDecodeSubscriber = true;
                }
            }

            if (realtimeTasks.Count > 0)
                await Task.WhenAll(realtimeTasks);

            if (hasBatchedDecodeSubscriber && wsjtxDecodeMessage.Decode is not null)
                _wsjtxDecodeCache.Add(wsjtxDecodeMessage.Decode);

            return;
        }
        
        var tasks = new List<Task>();

        using (var readerLock = await _pluginLock.ReaderLockAsync(token))
        {
            foreach (var pluginInfo in _plugins)
            {
                tasks.Add(pluginInfo.SendMessage(message, token));
            }
        }

        await Task.WhenAll(tasks);
    }

    
    public async Task<IReadOnlyList<PluginInfo>> GetConnectedPluginsAsync()
    {
        using (var readerLock = await _pluginLock.ReaderLockAsync())
        {
            return _plugins.ToList().AsReadOnly();
        }
    }
    
    private async Task _startService(CancellationToken token)
    {
        var scanTask = _scanAndRegisterPlugins(token);
        var heartbeatTask = _checkHeartBeat(token);
        await Task.WhenAll(scanTask, heartbeatTask);
    }

    private async Task _scanAndRegisterPlugins(CancellationToken cancellationToken)
    {
        var pipePath = OperatingSystem.IsWindows()
            ? DefaultConfigs.PluginRegisterNamingPipeWindows
            : DefaultConfigs.PluginRegisterNamingPipeUnix;
        
        ClassLogger.Debug("Starting plugin scanning service");

        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    pipePath,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096, 4096);

                await pipeServer.WaitForConnectionAsync(cancellationToken);
                await ProcessPluginRegistration(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (pipeServer is not null)
                {
                    await pipeServer.DisposeAsync();
                }
                ClassLogger.Info("Plugin reg service stopped");
                break;
            }
            catch (Exception e)
            {
                if (pipeServer is not null)
                {
                    await pipeServer.DisposeAsync();
                }
                ClassLogger.Error(e, "Plugin registration server error");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task ProcessPluginRegistration(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        var ownershipTransferred = false;
        try
        {
            using var link = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            link.CancelAfter(TimeSpan.FromSeconds(10));

            var transferred = ownershipTransferred;
            using var reg = link.Token.Register(() =>
            {
                if (!transferred) server.Dispose();
            });

            var pluginRegisterInfo = await PipeRegisterPluginReq.Parser.ParseDelimitedFromAsync(server, link.Token);
            if (pluginRegisterInfo is null)
            {
                throw new Exception("Plugin register info is null");
            }
            
            ClassLogger.Trace($"Plugin register request received: {pluginRegisterInfo.Name}");
            var registerServerInfo = await BuildServerInfoAsync(link.Token);
            registerServerInfo.ConnectedPluginCount += 1;
            await _sendResponse(server, true, null, registerServerInfo, link.Token);

            var pluginInfo = PluginInfo.Create(
                pluginRegisterInfo,
                server,
                cancellationToken,
                HandleControlRequestAsync,
                HandlePluginLog);
            ownershipTransferred = true;

            List<PluginInfo> replacedPlugins;
            using (var writerLock = await _pluginLock.WriterLockAsync(cancellationToken))
            {
                replacedPlugins = _plugins.Where(x => x.Uuid == pluginInfo.Uuid).ToList();
                foreach (var replaced in replacedPlugins)
                {
                    _plugins.Remove(replaced);
                }
                _plugins.Add(pluginInfo);
            }

            foreach (var replaced in replacedPlugins)
            {
                ClassLogger.Info($"Replacing duplicate plugin uuid {replaced.Uuid} ({replaced.Name})");
                replaced.Dispose();
                await BroadcastPluginLifecycleAsync(
                    replaced,
                    ClhPluginLifecycleEventType.Replaced,
                    "replaced-by-new-instance",
                    CancellationToken.None);
            }
            
            await BroadcastPluginLifecycleAsync(
                pluginInfo,
                ClhPluginLifecycleEventType.Connected,
                "registered",
                CancellationToken.None);
            await BroadcastServerStatusAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Parsing plugin register info failed");
            if (cancellationToken.IsCancellationRequested)
            {
                ClassLogger.Info("ProcessPluginRegistration stopped");
                return;
            }

            try
            {
                var serverInfo = await BuildServerInfoAsync(CancellationToken.None);
                await _sendResponse(server, false, e.Message, serverInfo, CancellationToken.None);
            }
            catch (Exception ex)
            {
                ClassLogger.Warn(ex, "Failed sending plugin register error response");
            }
        }
        finally
        {
            if (!ownershipTransferred)
            {
                try
                {
                    await server.DisposeAsync();
                }
                catch (Exception ex)
                {
                    ClassLogger.Warn(ex, "Failed to dispose pipe after registration failure");
                }
            }
        }
    }

    private async Task _sendResponse(
        NamedPipeServerStream server,
        bool success,
        string? message,
        PipeServerInfo serverInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = new PipeRegisterPluginResp
            {
                Success = success,
                ClhInstanceId = _basicSettings.InstanceName,
                Message = message ?? string.Empty,
                ServerInfo = serverInfo,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await response.WriteDelimitedToAsync(server, cancellationToken);
            await server.FlushAsync(cancellationToken);
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Failed to send response to plugin");
        }
    }

    private async Task HandleControlRequestAsync(
        PluginInfo pluginInfo,
        PipeControlRequest request,
        CancellationToken token)
    {
        var response = new PipeControlResponse
        {
            RequestId = string.IsNullOrWhiteSpace(request.RequestId)
                ? Guid.NewGuid().ToString("N")
                : request.RequestId,
            Success = false,
            Message = string.Empty,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        try
        {
            if (!pluginInfo.HasCapability(Capability.PipeControl))
            {
                response.Message = "Plugin has not declared PIPE_CONTROL capability.";
                await pluginInfo.SendMessage(response, token);
                return;
            }

            switch (request.Command)
            {
                case PipeControlCommand.GetServerInfo:
                    response.Success = true;
                    response.Message = "ok";
                    response.ServerInfo = await BuildServerInfoAsync(token);
                    break;

                case PipeControlCommand.GetConnectedPlugins:
                    response.Success = true;
                    response.Message = "ok";
                    response.ConnectedPlugins = await BuildPluginListAsync(token);
                    break;

                case PipeControlCommand.SetWsjtxSubscription:
                    pluginInfo.UpdateWsjtxSubscription(request.WsjtxSubscription);
                    response.Success = true;
                    response.Message = "subscription-updated";
                    response.WsjtxSubscription = pluginInfo.GetWsjtxSubscriptionSnapshot();
                    break;

                default:
                    response.Success = false;
                    response.Message = $"Unsupported command: {request.Command}";
                    break;
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = ex.Message;
            ClassLogger.Error(ex, $"Error handling control request for plugin {pluginInfo.Name}");
        }

        await pluginInfo.SendMessage(response, token);
    }

    private void HandlePluginLog(PluginInfo pluginInfo, PipePluginLog pluginLog)
    {
        var fieldPart = pluginLog.Fields.Count == 0
            ? string.Empty
            : " | " + string.Join(", ", pluginLog.Fields.Select(x => $"{x.Key}={x.Value}"));
        var msg = $"[{pluginInfo.Name}/{pluginInfo.Uuid}] {pluginLog.Message}{fieldPart}";

        switch (pluginLog.Level)
        {
            case PipePluginLogLevel.Debug:
                ClassLogger.Debug(msg);
                break;
            case PipePluginLogLevel.Warn:
                ClassLogger.Warn(msg);
                break;
            case PipePluginLogLevel.Error:
                ClassLogger.Error(msg);
                break;
            case PipePluginLogLevel.Info:
            case PipePluginLogLevel.Unspecified:
            default:
                ClassLogger.Info(msg);
                break;
        }
    }

    private async Task<PipeServerInfo> BuildServerInfoAsync(CancellationToken token)
    {
        uint count;
        using (var readerLock = await _pluginLock.ReaderLockAsync(token))
        {
            count = (uint)_plugins.Count;
        }

        return new PipeServerInfo
        {
            ClhInstanceId = _basicSettings.InstanceName,
            ClhVersion = VersionInfo.Version,
            KeepaliveTimeoutSec = (uint)DefaultConfigs.PluginKeepaliveTimeoutSec,
            ConnectedPluginCount = count
        };
    }

    private async Task<PipePluginList> BuildPluginListAsync(CancellationToken token)
    {
        var list = new PipePluginList();
        using (var readerLock = await _pluginLock.ReaderLockAsync(token))
        {
            foreach (var plugin in _plugins)
            {
                list.Plugins.Add(plugin.ToPipePluginInfo());
            }
        }

        return list;
    }

    private async Task BroadcastPluginLifecycleAsync(
        PluginInfo pluginInfo,
        ClhPluginLifecycleEventType eventType,
        string reason,
        CancellationToken token)
    {
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var internalMessage = new ClhInternalMessage
        {
            Timestamp = now,
            PluginLifecycle = new ClhPluginLifecycleChanged
            {
                PluginUuid = pluginInfo.Uuid,
                PluginName = pluginInfo.Name,
                PluginVersion = pluginInfo.Version,
                Reason = reason,
                EventType = eventType,
                EventTime = now
            }
        };

        await BroadcastMessageAsync(internalMessage, token);
    }

    private async Task BroadcastServerStatusAsync(CancellationToken token)
    {
        var serverInfo = await BuildServerInfoAsync(token);
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        var message = new ClhInternalMessage
        {
            Timestamp = now,
            ServerStatus = new ClhServerStatusChanged
            {
                ClhInstanceId = serverInfo.ClhInstanceId,
                ClhVersion = serverInfo.ClhVersion,
                ConnectedPluginCount = serverInfo.ConnectedPluginCount,
                EventTime = now
            }
        };

        await BroadcastMessageAsync(message, token);
    }

    private async Task _checkHeartBeat(CancellationToken cancellationToken)
    {
        ClassLogger.Debug("Starting plugin keepalive service");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                ClassLogger.Trace("Checking heartbeat...");
                var toRemove = new List<PluginInfo>();

                using (var readerLock = await _pluginLock.ReaderLockAsync(cancellationToken))
                {
                    foreach (var pluginInfo in _plugins)
                    {
                        if (DateTime.UtcNow - pluginInfo.LastHeartbeat >
                            TimeSpan.FromSeconds(DefaultConfigs.PluginKeepaliveTimeoutSec))
                        {
                            ClassLogger.Info($"{pluginInfo.Name} timeout.");
                            toRemove.Add(pluginInfo);
                        }
                    }
                }

                if (toRemove.Count == 0)
                {
                    ClassLogger.Trace("No plugin timeout detected.");
                    continue;
                }

                using (var writerLock = await _pluginLock.WriterLockAsync(cancellationToken))
                {
                    foreach (var plugin in toRemove)
                    {
                        _plugins.Remove(plugin);
                    }
                }

                foreach (var plugin in toRemove)
                {
                    plugin.Dispose();
                    await BroadcastPluginLifecycleAsync(
                        plugin,
                        ClhPluginLifecycleEventType.Timeout,
                        "heartbeat-timeout",
                        CancellationToken.None);
                }

                await BroadcastServerStatusAsync(CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ClassLogger.Info("Plugin heartbeat service stopped");
                break;
            }
            catch (Exception e)
            {
                ClassLogger.Error(e, "Error in heartbeat check");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}
