using System;
using System.Collections.Concurrent;
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
using CloudlogHelper.ViewModels;
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
    private readonly Func<PluginInfo, PipeEnvelope, CancellationToken, Task> _envelopeHandler;
    private readonly Action<PluginInfo, PipePluginLog> _pluginLogHandler;
    private readonly Func<string, Task> _selfDeregister;
    private readonly object _subscriptionLock = new();
    private readonly Dictionary<string, string> _metadata = new();

    private NamedPipeServerStream? _client;
    private readonly SemaphoreSlim _cliLock = new(1, 1);

    private Task? _messageLoopTask;
    private CancellationTokenSource? _messageLoopCts;
    private bool _disposed;
    private long _lastHeartbeatTicks;
    private long _receivedMessageCount;
    private long _sentMessageCount;
    private long _controlRequestCount;
    private long _controlErrorCount;
    private long _lastRoundtripMs;

    private MessageType[]? _wsjtxMessageTypes;
    private DecodeDeliveryMode _decodeDeliveryMode = DecodeDeliveryMode.Batched;
    private PipeEnvelopeTopic[]? _eventTopics;
    private bool _includeSnapshot;

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
        Func<string, Task> selfDeregister,
        Func<PluginInfo, PipeControlRequest, CancellationToken, Task> controlRequestHandler,
        Func<PluginInfo, PipeEnvelope, CancellationToken, Task> envelopeHandler,
        Action<PluginInfo, PipePluginLog> pluginLogHandler)
    {
        _controlRequestHandler = controlRequestHandler;
        _envelopeHandler = envelopeHandler;
        _pluginLogHandler = pluginLogHandler;
        _selfDeregister = selfDeregister;
    }
    
    private void UpdateHeartbeat()
    {
        Interlocked.Exchange(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);
    }

    private void IncrementReceived()
    {
        Interlocked.Increment(ref _receivedMessageCount);
    }

    private void IncrementSent()
    {
        Interlocked.Increment(ref _sentMessageCount);
    }

    public void IncrementControlRequest()
    {
        Interlocked.Increment(ref _controlRequestCount);
    }

    public void IncrementControlError()
    {
        Interlocked.Increment(ref _controlErrorCount);
    }

    public void MarkRoundtrip(Timestamp? sourceTimestamp)
    {
        if (sourceTimestamp is null) return;
        var delta = DateTime.UtcNow - sourceTimestamp.ToDateTime();
        if (delta.TotalMilliseconds < 0) return;
        Interlocked.Exchange(ref _lastRoundtripMs, (long)delta.TotalMilliseconds);
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
                    await _selfDeregister(Uuid);
                    break;
                }
                
                IncrementReceived();

                if (anyMessage.Is(PipeHeartbeat.Descriptor))
                {
                    var heartbeat = anyMessage.Unpack<PipeHeartbeat>();
                    if (!string.IsNullOrWhiteSpace(heartbeat.Uuid) && heartbeat.Uuid != Uuid)
                    {
                        ClassLogger.Warn(
                            $"Heartbeat uuid mismatch for plugin {Name}. Expected {Uuid}, got {heartbeat.Uuid}");
                    }
                    
                    UpdateHeartbeat();
                    continue;
                }

                if (anyMessage.Is(PipeControlRequest.Descriptor))
                {
                    var request = anyMessage.Unpack<PipeControlRequest>();
                    IncrementControlRequest();
                    MarkRoundtrip(request.Timestamp);
                    await _controlRequestHandler(this, request, token);
                    continue;
                }

                if (anyMessage.Is(PipeEnvelope.Descriptor))
                {
                    var envelope = anyMessage.Unpack<PipeEnvelope>();
                    if (envelope.Kind is PipeEnvelopeKind.Query or PipeEnvelopeKind.Command)
                    {
                        IncrementControlRequest();
                        MarkRoundtrip(envelope.Timestamp);
                    }
                    await _envelopeHandler(this, envelope, token);
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
            catch (OperationCanceledException)
            {   
                break;
            }
            catch (IOException) when (!token.IsCancellationRequested)
            {
                ClassLogger.Info($"{Name} exited due to IO exception");
                await _selfDeregister(Uuid);
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
        Func<PluginInfo, PipeEnvelope, CancellationToken, Task> envelopeHandler,
        Action<PluginInfo, PipePluginLog> pluginLogHandler,
        Func<string,Task> selfDeregister)
    {
        var pg = new PluginInfo(selfDeregister, controlRequestHandler, envelopeHandler, pluginLogHandler)
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
        pg.UpdateEventSubscription(rpcRegisterPluginReq.EventSubscription);
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

    public void UpdateEventSubscription(PipeEventSubscription? subscription)
    {
        if (subscription is null) return;
        lock (_subscriptionLock)
        {
            _includeSnapshot = subscription.IncludeSnapshot;
            _eventTopics = subscription.Topics.Distinct().ToArray();
        }
    }

    public PipeEventSubscription GetEventSubscriptionSnapshot()
    {
        var snapshot = new PipeEventSubscription();
        lock (_subscriptionLock)
        {
            snapshot.IncludeSnapshot = _includeSnapshot;
            if (_eventTopics != null)
                snapshot.Topics.Add(_eventTopics);
        }

        return snapshot;
    }

    public bool ShouldReceiveEventTopic(PipeEnvelopeTopic topic)
    {
        PipeEnvelopeTopic[]? topics;
        lock (_subscriptionLock)
        {
            topics = _eventTopics;
        }

        if (topics is null || topics.Length == 0) return true;
        return Array.IndexOf(topics, topic) >= 0;
    }

    public bool WantsEventSnapshot()
    {
        lock (_subscriptionLock)
        {
            return _includeSnapshot;
        }
    }

    public PipePluginTelemetry GetTelemetrySnapshot()
    {
        return new PipePluginTelemetry
        {
            PluginUuid = Uuid,
            ReceivedMessageCount = (ulong)Interlocked.Read(ref _receivedMessageCount),
            SentMessageCount = (ulong)Interlocked.Read(ref _sentMessageCount),
            ControlRequestCount = (ulong)Interlocked.Read(ref _controlRequestCount),
            ControlErrorCount = (ulong)Interlocked.Read(ref _controlErrorCount),
            LastRoundtripMs = (uint)Math.Max(0, Interlocked.Read(ref _lastRoundtripMs)),
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
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
            WsjtxSubscription = GetWsjtxSubscriptionSnapshot(),
            EventSubscription = GetEventSubscriptionSnapshot(),
            Telemetry = GetTelemetrySnapshot()
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
            case PipeEnvelope envelope:
                if (envelope.Kind == PipeEnvelopeKind.Event)
                {
                    if (!HasCapability(Capability.EventSubscription)) return;
                    if (!ShouldReceiveEventTopic(envelope.Topic)) return;
                }
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
                IncrementSent();
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
        Task.Run(StopAll).GetAwaiter().GetResult();
    }
}

public class PluginService : IPluginService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly List<PluginInfo> _plugins = new();
    private readonly AsyncReaderWriterLock _pluginLock = new();

    private readonly IApplicationSettingsService _settingsService;
    private readonly IRigBackendManager _rigBackendManager;
    private readonly IUdpServerService _udpServerService;
    private readonly IQSOUploadService _qsoUploadService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly IInAppNotificationService _notificationService;

    private CancellationTokenSource? _source;
    private Task? _pluginTask;
    private readonly object _serviceLock = new();
    private bool _isRunning;
    private DateTime _serviceStartedAt = DateTime.UtcNow;
    
    private readonly ObservableCollection<Decode> _wsjtxDecodeCache = new();
    
    private readonly BasicSettings _basicSettings;
    private readonly CompositeDisposable _settingsSubscription = new();
    private long _qsoUploadSuccessTotal;
    private long _qsoUploadFailTotal;
    
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObservableCollection<Decode>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<PluginInfo>))]
    public PluginService(
        IApplicationSettingsService settingsService,
        IRigBackendManager rigBackendManager,
        IUdpServerService udpServerService,
        IQSOUploadService qsoUploadService,
        IWindowManagerService windowManagerService,
        IInAppNotificationService notificationService)
    {
        _settingsService = settingsService;
        _rigBackendManager = rigBackendManager;
        _udpServerService = udpServerService;
        _qsoUploadService = qsoUploadService;
        _windowManagerService = windowManagerService;
        _notificationService = notificationService;
        _basicSettings = settingsService.GetCurrentSettings().BasicSettings;
        
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
                .Subscribe(async x =>
                {
                    await HandleSettingsChange(x);
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

    private async Task HandleSettingsChange(SettingsChanged changed)
    {
        try
        {
            if (changed.Part == ChangedPart.BasicSettings && _basicSettings.EnablePlugin && !_isRunning)
            {
                ClassLogger.Info("Plugin service enabled via settings change");
                await StartPluginServiceAsync();
            }
            else if (changed.Part == ChangedPart.BasicSettings && !_basicSettings.EnablePlugin && _isRunning)
            {
                ClassLogger.Info("Plugin service disabled via settings change");
                await StopPluginServiceAsync();
                return;
            }

            if (!_isRunning) return;

            await BroadcastSettingsChangeAsync(changed.Part, "settings-updated", CancellationToken.None);
            if (changed.Part == ChangedPart.RigService)
            {
                await BroadcastRigServiceStatusAsync("settings-updated", CancellationToken.None);
            }
            else if (changed.Part == ChangedPart.UDPServer)
            {
                await BroadcastUdpServiceStatusAsync("settings-updated", CancellationToken.None);
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
                _serviceStartedAt = DateTime.UtcNow;
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

        if (message is ClhInternalMessage internalMessage &&
            internalMessage.PayloadCase == ClhInternalMessage.PayloadOneofCase.QsoUploadStatus)
        {
            switch (internalMessage.QsoUploadStatus?.UploadStatus ?? SydneyOwl.CLHProto.Plugin.UploadStatus.Unspecified)
            {
                case SydneyOwl.CLHProto.Plugin.UploadStatus.Success:
                    Interlocked.Increment(ref _qsoUploadSuccessTotal);
                    break;
                case SydneyOwl.CLHProto.Plugin.UploadStatus.Fail:
                    Interlocked.Increment(ref _qsoUploadFailTotal);
                    break;
            }
        }

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

            await TryBroadcastEnvelopeEventAsync(wsjtxDecodeMessage, token);
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
        await TryBroadcastEnvelopeEventAsync(message, token);
    }

    
    public async Task<IReadOnlyList<PluginInfo>> GetConnectedPluginsAsync()
    {
        using (var readerLock = await _pluginLock.ReaderLockAsync())
        {
            return _plugins.ToList().AsReadOnly();
        }
    }

    public Task<PipeServerInfo> GetServerInfoAsync(CancellationToken token)
    {
        return BuildServerInfoAsync(token);
    }

    public Task<PipeRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken token)
    {
        return BuildRuntimeSnapshotAsync(token);
    }

    public async Task<bool> DisconnectPluginAsync(string pluginUuid, string reason, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(pluginUuid))
        {
            return false;
        }

        PluginInfo? target = null;
        using (var writerLock = await _pluginLock.WriterLockAsync(token))
        {
            target = _plugins.FirstOrDefault(x => x.Uuid == pluginUuid);
            if (target != null)
            {
                _plugins.Remove(target);
            }
        }

        if (target == null)
        {
            return false;
        }

        target.Dispose();
        await BroadcastPluginLifecycleAsync(
            target,
            ClhPluginLifecycleEventType.Disconnected,
            reason,
            CancellationToken.None);
        await BroadcastServerStatusAsync(CancellationToken.None);
        return true;
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
                HandleEnvelopeRequestAsync,
                HandlePluginLog,
                uuid => DisconnectPluginAsync(uuid, "Plugin closed connection", CancellationToken.None));
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
            await SendInitialSnapshotsAsync(pluginInfo, CancellationToken.None);
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
                pluginInfo.IncrementControlError();
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

                case PipeControlCommand.GetRuntimeSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.RuntimeSnapshot = await BuildRuntimeSnapshotAsync(token);
                    break;

                case PipeControlCommand.GetRigSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.RigSnapshot = await BuildRigSnapshotAsync(token);
                    break;

                case PipeControlCommand.GetUdpSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.UdpSnapshot = await BuildUdpSnapshotAsync(token);
                    break;

                case PipeControlCommand.GetQsoQueueSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.QsoQueueSnapshot = await BuildQsoQueueSnapshotAsync(token);
                    break;

                case PipeControlCommand.GetSettingsSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.SettingsSnapshot = BuildSettingsSnapshot();
                    break;

                case PipeControlCommand.GetPluginTelemetry:
                    response.Success = true;
                    response.Message = "ok";
                    response.PluginTelemetry = await SelectPluginTelemetryAsync(
                        pluginInfo,
                        request.Arguments.TryGetValue("plugin_uuid", out var pluginUuid) ? pluginUuid : null,
                        token) ?? pluginInfo.GetTelemetrySnapshot();
                    break;

                case PipeControlCommand.SubscribeEvents:
                    pluginInfo.UpdateEventSubscription(request.EventSubscription);
                    response.Success = true;
                    response.Message = "event-subscription-updated";
                    await SendInitialSnapshotsAsync(pluginInfo, token);
                    break;

                case PipeControlCommand.ShowMainWindow:
                    await ExecuteMainWindowVisibilityAsync(true);
                    response.Success = true;
                    response.Message = "main-window-shown";
                    response.RuntimeSnapshot = await BuildRuntimeSnapshotAsync(token);
                    break;

                case PipeControlCommand.HideMainWindow:
                    await ExecuteMainWindowVisibilityAsync(false);
                    response.Success = true;
                    response.Message = "main-window-hidden";
                    response.RuntimeSnapshot = await BuildRuntimeSnapshotAsync(token);
                    break;

                case PipeControlCommand.OpenSettingsWindow:
                    await _windowManagerService.CreateAndShowWindowByVm(typeof(SettingsWindowViewModel));
                    response.Success = true;
                    response.Message = "settings-window-opened";
                    break;

                case PipeControlCommand.SendNotification:
                {
                    var notify = request.Notification ?? new PipeNotificationCommand
                    {
                        Level = PipeNotificationLevel.Info,
                        Title = request.Arguments.TryGetValue("title", out var title) ? title : "Plugin Notification",
                        Message = request.Arguments.TryGetValue("message", out var message) ? message : string.Empty
                    };

                    await SendNotificationAsync(notify);
                    response.Success = true;
                    response.Message = "notification-sent";
                    break;
                }

                case PipeControlCommand.ToggleUdpServer:
                {
                    var enabled = !_udpServerService.IsUdpServerEnabled();
                    if (request.Arguments.TryGetValue("enabled", out var enabledRaw) &&
                        bool.TryParse(enabledRaw, out var parsedEnabled))
                    {
                        enabled = parsedEnabled;
                    }

                    var patch = new PipeSettingsPatch();
                    patch.Values["udp.enable_udp_server"] = enabled.ToString();
                    var patchResult = ApplySettingsPatch(patch);
                    response.Success = patchResult.Success;
                    response.Message = patchResult.Message;
                    response.UdpSnapshot = await BuildUdpSnapshotAsync(token);
                    break;
                }

                case PipeControlCommand.StartRigBackend:
                    await _rigBackendManager.StartService();
                    response.Success = true;
                    response.Message = "rig-backend-started";
                    response.RigSnapshot = await BuildRigSnapshotAsync(token);
                    await BroadcastRigServiceStatusAsync("rig-started", CancellationToken.None);
                    break;

                case PipeControlCommand.StopRigBackend:
                    await _rigBackendManager.StopService();
                    response.Success = true;
                    response.Message = "rig-backend-stopped";
                    response.RigSnapshot = await BuildRigSnapshotAsync(token);
                    await BroadcastRigServiceStatusAsync("rig-stopped", CancellationToken.None);
                    break;

                case PipeControlCommand.RestartRigBackend:
                    await _rigBackendManager.RestartService();
                    response.Success = true;
                    response.Message = "rig-backend-restarted";
                    response.RigSnapshot = await BuildRigSnapshotAsync(token);
                    await BroadcastRigServiceStatusAsync("rig-restarted", CancellationToken.None);
                    break;

                case PipeControlCommand.TriggerQsoReupload:
                    await _qsoUploadService.StartAsync(token);
                    response.Success = true;
                    response.Message = "qso-upload-service-started";
                    response.QsoQueueSnapshot = await BuildQsoQueueSnapshotAsync(token);
                    await BroadcastQsoQueueStatusAsync("qso-upload-service-started", CancellationToken.None);
                    break;

                case PipeControlCommand.UpdateSettings:
                {
                    var patch = request.SettingsPatch ?? new PipeSettingsPatch();
                    if (patch.Values.Count == 0 && request.Arguments.Count > 0)
                    {
                        foreach (var kv in request.Arguments)
                        {
                            patch.Values[kv.Key] = kv.Value;
                        }
                    }

                    var patchResult = ApplySettingsPatch(patch);
                    response.Success = patchResult.Success;
                    response.Message = patchResult.Message;
                    response.SettingsSnapshot = BuildSettingsSnapshot();
                    break;
                }

                default:
                    response.Success = false;
                    response.Message = $"Unsupported command: {request.Command}";
                    break;
            }

            if (!response.Success)
            {
                pluginInfo.IncrementControlError();
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = ex.Message;
            pluginInfo.IncrementControlError();
            ClassLogger.Error(ex, $"Error handling control request for plugin {pluginInfo.Name}");
        }

        await pluginInfo.SendMessage(response, token);
        await BroadcastPluginTelemetryChangedAsync(pluginInfo, CancellationToken.None);
    }

    private async Task HandleEnvelopeRequestAsync(PluginInfo pluginInfo, PipeEnvelope envelope, CancellationToken token)
    {
        if (envelope.Kind is not (PipeEnvelopeKind.Query or PipeEnvelopeKind.Command))
        {
            return;
        }

        var response = new PipeEnvelope
        {
            Id = Guid.NewGuid().ToString("N"),
            CorrelationId = string.IsNullOrWhiteSpace(envelope.Id) ? envelope.CorrelationId : envelope.Id,
            Kind = PipeEnvelopeKind.Response,
            Topic = envelope.Topic,
            Success = false,
            Message = string.Empty,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        try
        {
            if (!pluginInfo.HasCapability(Capability.PipeControl))
            {
                response.Success = false;
                response.ErrorCode = "capability_denied";
                response.Message = "Plugin has not declared PIPE_CONTROL capability.";
                pluginInfo.IncrementControlError();
                await pluginInfo.SendMessage(response, token);
                return;
            }

            switch (envelope.Topic)
            {
                case PipeEnvelopeTopic.QueryServerInfo:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(await BuildServerInfoAsync(token));
                    break;
                case PipeEnvelopeTopic.QueryConnectedPlugins:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(await BuildPluginListAsync(token));
                    break;
                case PipeEnvelopeTopic.QueryWsjtxSubscription:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(pluginInfo.GetWsjtxSubscriptionSnapshot());
                    break;
                case PipeEnvelopeTopic.QueryRuntimeSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(await BuildRuntimeSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.QueryRigSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(await BuildRigSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.QueryUdpSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(await BuildUdpSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.QueryQsoQueueSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(await BuildQsoQueueSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.QuerySettingsSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(BuildSettingsSnapshot());
                    break;
                case PipeEnvelopeTopic.QueryPluginTelemetry:
                {
                    var telemetry = await SelectPluginTelemetryAsync(
                        pluginInfo,
                        envelope.Attributes.TryGetValue("plugin_uuid", out var pluginUuid) ? pluginUuid : null,
                        token) ?? pluginInfo.GetTelemetrySnapshot();
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(telemetry);
                    break;
                }
                case PipeEnvelopeTopic.CommandSetWsjtxSubscription:
                {
                    if (envelope.Payload is null || !envelope.Payload.Is(PipeWsjtxSubscription.Descriptor))
                    {
                        response.Success = false;
                        response.ErrorCode = "invalid_payload";
                        response.Message = "PipeWsjtxSubscription payload is required";
                        break;
                    }

                    pluginInfo.UpdateWsjtxSubscription(envelope.Payload.Unpack<PipeWsjtxSubscription>());
                    response.Success = true;
                    response.Message = "subscription-updated";
                    response.Payload = Any.Pack(pluginInfo.GetWsjtxSubscriptionSnapshot());
                    break;
                }
                case PipeEnvelopeTopic.CommandSubscribeEvents:
                {
                    PipeEventSubscription? subscription = envelope.Subscription;
                    if (subscription is null && envelope.Payload != null && envelope.Payload.Is(PipeEventSubscription.Descriptor))
                    {
                        subscription = envelope.Payload.Unpack<PipeEventSubscription>();
                    }

                    if (subscription is null)
                    {
                        response.Success = false;
                        response.ErrorCode = "invalid_payload";
                        response.Message = "PipeEventSubscription payload is required";
                        break;
                    }

                    pluginInfo.UpdateEventSubscription(subscription);
                    await SendInitialSnapshotsAsync(pluginInfo, token);
                    response.Success = true;
                    response.Message = "event-subscription-updated";
                    response.Payload = Any.Pack(pluginInfo.GetEventSubscriptionSnapshot());
                    break;
                }
                case PipeEnvelopeTopic.CommandShowMainWindow:
                    await ExecuteMainWindowVisibilityAsync(true);
                    response.Success = true;
                    response.Message = "main-window-shown";
                    break;
                case PipeEnvelopeTopic.CommandHideMainWindow:
                    await ExecuteMainWindowVisibilityAsync(false);
                    response.Success = true;
                    response.Message = "main-window-hidden";
                    break;
                case PipeEnvelopeTopic.CommandOpenSettingsWindow:
                    await _windowManagerService.CreateAndShowWindowByVm(typeof(SettingsWindowViewModel));
                    response.Success = true;
                    response.Message = "settings-window-opened";
                    break;
                case PipeEnvelopeTopic.CommandSendNotification:
                {
                    if (envelope.Payload is null || !envelope.Payload.Is(PipeNotificationCommand.Descriptor))
                    {
                        response.Success = false;
                        response.ErrorCode = "invalid_payload";
                        response.Message = "PipeNotificationCommand payload is required";
                        break;
                    }

                    await SendNotificationAsync(envelope.Payload.Unpack<PipeNotificationCommand>());
                    response.Success = true;
                    response.Message = "notification-sent";
                    break;
                }
                case PipeEnvelopeTopic.CommandToggleUdpServer:
                {
                    var enabled = !_udpServerService.IsUdpServerEnabled();
                    if (envelope.Attributes.TryGetValue("enabled", out var enabledRaw) &&
                        bool.TryParse(enabledRaw, out var parsedEnabled))
                    {
                        enabled = parsedEnabled;
                    }

                    var patch = new PipeSettingsPatch();
                    patch.Values["udp.enable_udp_server"] = enabled.ToString();
                    var patchResult = ApplySettingsPatch(patch);
                    response.Success = patchResult.Success;
                    response.Message = patchResult.Message;
                    if (response.Success)
                    {
                        response.Payload = Any.Pack(await BuildUdpSnapshotAsync(token));
                    }
                    break;
                }
                case PipeEnvelopeTopic.CommandStartRigBackend:
                    await _rigBackendManager.StartService();
                    response.Success = true;
                    response.Message = "rig-backend-started";
                    response.Payload = Any.Pack(await BuildRigSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.CommandStopRigBackend:
                    await _rigBackendManager.StopService();
                    response.Success = true;
                    response.Message = "rig-backend-stopped";
                    response.Payload = Any.Pack(await BuildRigSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.CommandRestartRigBackend:
                    await _rigBackendManager.RestartService();
                    response.Success = true;
                    response.Message = "rig-backend-restarted";
                    response.Payload = Any.Pack(await BuildRigSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.CommandTriggerQsoReupload:
                    await _qsoUploadService.StartAsync(token);
                    response.Success = true;
                    response.Message = "qso-upload-service-started";
                    response.Payload = Any.Pack(await BuildQsoQueueSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.CommandUpdateSettings:
                {
                    PipeSettingsPatch patch;
                    if (envelope.Payload != null && envelope.Payload.Is(PipeSettingsPatch.Descriptor))
                    {
                        patch = envelope.Payload.Unpack<PipeSettingsPatch>();
                    }
                    else
                    {
                        patch = new PipeSettingsPatch();
                        foreach (var kv in envelope.Attributes)
                        {
                            patch.Values[kv.Key] = kv.Value;
                        }
                    }

                    var patchResult = ApplySettingsPatch(patch);
                    response.Success = patchResult.Success;
                    response.Message = patchResult.Message;
                    if (response.Success)
                    {
                        response.Payload = Any.Pack(BuildSettingsSnapshot());
                    }
                    break;
                }
                default:
                    response.Success = false;
                    response.ErrorCode = "unsupported_topic";
                    response.Message = $"Unsupported topic: {envelope.Topic}";
                    break;
            }

            if (!response.Success)
            {
                pluginInfo.IncrementControlError();
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.ErrorCode = "internal_error";
            response.Message = ex.Message;
            pluginInfo.IncrementControlError();
            ClassLogger.Error(ex, "Error handling envelope request");
        }

        await pluginInfo.SendMessage(response, token);
        await BroadcastPluginTelemetryChangedAsync(pluginInfo, CancellationToken.None);
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

        var uptime = DateTime.UtcNow - _serviceStartedAt;
        var uptimeSec = uptime.TotalSeconds < 0 ? 0UL : (ulong)uptime.TotalSeconds;

        return new PipeServerInfo
        {
            ClhInstanceId = _basicSettings.InstanceName,
            ClhVersion = VersionInfo.Version,
            KeepaliveTimeoutSec = (uint)DefaultConfigs.PluginKeepaliveTimeoutSec,
            ConnectedPluginCount = count,
            UptimeSec = uptimeSec
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

    private async Task<PipeRuntimeSnapshot> BuildRuntimeSnapshotAsync(CancellationToken token)
    {
        var serverTask = BuildServerInfoAsync(token);
        var rigTask = BuildRigSnapshotAsync(token);
        var udpTask = BuildUdpSnapshotAsync(token);
        var qsoTask = BuildQsoQueueSnapshotAsync(token);

        await Task.WhenAll(serverTask, rigTask, udpTask, qsoTask);

        var snapshot = new PipeRuntimeSnapshot
        {
            ServerInfo = await serverTask,
            RigSnapshot = await rigTask,
            UdpSnapshot = await udpTask,
            QsoQueueSnapshot = await qsoTask,
            SettingsSnapshot = BuildSettingsSnapshot(),
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        using (var readerLock = await _pluginLock.ReaderLockAsync(token))
        {
            foreach (var plugin in _plugins)
            {
                snapshot.PluginTelemetry.Add(plugin.GetTelemetrySnapshot());
            }
        }

        return snapshot;
    }

    private async Task<PipeRigSnapshot> BuildRigSnapshotAsync(CancellationToken token)
    {
        var snapshot = new PipeRigSnapshot
        {
            Provider = _rigBackendManager.GetServiceType().ToString(),
            Endpoint = _rigBackendManager.GetServiceEndpointAddress(),
            ServiceRunning = _rigBackendManager.IsServiceRunning(),
            PollingEnabled = _rigBackendManager.GetPollingAllowed(),
            PollingIntervalSec = (uint)Math.Max(0, _rigBackendManager.GetPollingInterval()),
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        try
        {
            var data = await _rigBackendManager.GetAllRigInfo();
            snapshot.TxFrequencyHz = SafeToUlong(data.FrequencyTx);
            snapshot.RxFrequencyHz = SafeToUlong(data.FrequencyRx);
            snapshot.TxMode = data.ModeTx ?? string.Empty;
            snapshot.RxMode = data.ModeRx ?? string.Empty;
            snapshot.Split = data.IsSplit;
            snapshot.Power = SafeToUint(data.Power ?? 0);
        }
        catch (Exception ex)
        {
            ClassLogger.Debug(ex, "Failed sampling rig snapshot");
        }

        return snapshot;
    }

    private Task<PipeUdpSnapshot> BuildUdpSnapshotAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settings = _settingsService.GetCurrentSettings().UDPSettings;
        return Task.FromResult(new PipeUdpSnapshot
        {
            ServerEnabled = _udpServerService.IsUdpServerEnabled(),
            ServerRunning = _udpServerService.IsUdpServerRunning(),
            AllowExternal = settings.EnableConnectionFromOutside,
            BindAddress = _udpServerService.GetUdpBindingAddress(),
            RetryCount = (uint)Math.Max(0, _udpServerService.QSOUploadRetryCount()),
            NotifyOnQsoMade = _udpServerService.IsNotifyOnQsoMade(),
            NotifyOnQsoUploaded = _udpServerService.IsNotifyOnQsoUploaded(),
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    private async Task<PipeQsoQueueSnapshot> BuildQsoQueueSnapshotAsync(CancellationToken token)
    {
        var pending = await _qsoUploadService.GetPendingCountAsync();
        return new PipeQsoQueueSnapshot
        {
            PendingCount = (uint)Math.Max(0, pending),
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    private PipeSettingsSnapshot BuildSettingsSnapshot()
    {
        var settings = _settingsService.GetCurrentSettings();
        return new PipeSettingsSnapshot
        {
            InstanceName = settings.BasicSettings.InstanceName,
            Language = settings.BasicSettings.LanguageType.ToString(),
            EnablePlugin = settings.BasicSettings.EnablePlugin,
            DisableAllCharts = settings.BasicSettings.DisableAllCharts,
            MyMaidenheadGrid = settings.BasicSettings.MyMaidenheadGrid ?? string.Empty,
            AutoQsoUploadEnabled = settings.CloudlogSettings.AutoQSOUploadEnabled,
            AutoRigUploadEnabled = settings.CloudlogSettings.AutoRigUploadEnabled,
            EnableUdpServer = settings.UDPSettings.EnableUDPServer,
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    private async Task<PipePluginTelemetry?> SelectPluginTelemetryAsync(
        PluginInfo requester,
        string? pluginUuid,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(pluginUuid) || pluginUuid == requester.Uuid)
        {
            return requester.GetTelemetrySnapshot();
        }

        using (var readerLock = await _pluginLock.ReaderLockAsync(token))
        {
            return _plugins.FirstOrDefault(x => x.Uuid == pluginUuid)?.GetTelemetrySnapshot();
        }
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

    private async Task BroadcastRigServiceStatusAsync(string reason, CancellationToken token)
    {
        var rig = await BuildRigSnapshotAsync(token);
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var status = rig.ServiceRunning ? ClhServiceRunStatus.Running : ClhServiceRunStatus.Stopped;

        var message = new ClhInternalMessage
        {
            Timestamp = now,
            RigServiceStatus = new ClhRigServiceStatusChanged
            {
                BackendService = rig.Provider,
                Endpoint = rig.Endpoint,
                Status = status,
                PollingEnabled = rig.PollingEnabled,
                PollingIntervalSec = rig.PollingIntervalSec,
                Message = reason,
                EventTime = now
            }
        };

        await BroadcastMessageAsync(message, token);
    }

    private async Task BroadcastUdpServiceStatusAsync(string reason, CancellationToken token)
    {
        var udp = await BuildUdpSnapshotAsync(token);
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var status = udp.ServerRunning ? ClhServiceRunStatus.Running : ClhServiceRunStatus.Stopped;

        var message = new ClhInternalMessage
        {
            Timestamp = now,
            UdpServiceStatus = new ClhUdpServiceStatusChanged
            {
                BindAddress = udp.BindAddress,
                Status = status,
                AllowExternal = udp.AllowExternal,
                RetryCount = udp.RetryCount,
                NotifyOnQsoMade = udp.NotifyOnQsoMade,
                NotifyOnQsoUploaded = udp.NotifyOnQsoUploaded,
                Message = reason,
                EventTime = now
            }
        };

        await BroadcastMessageAsync(message, token);
    }

    private async Task BroadcastQsoQueueStatusAsync(string reason, CancellationToken token)
    {
        _ = reason;
        var queue = await BuildQsoQueueSnapshotAsync(token);
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        var message = new ClhInternalMessage
        {
            Timestamp = now,
            QsoQueueStatus = new ClhQsoQueueStatusChanged
            {
                PendingCount = queue.PendingCount,
                UploadedTotal = (ulong)Math.Max(0, Interlocked.Read(ref _qsoUploadSuccessTotal)),
                FailedTotal = (ulong)Math.Max(0, Interlocked.Read(ref _qsoUploadFailTotal)),
                EventTime = now
            }
        };

        await BroadcastMessageAsync(message, token);
    }

    private async Task BroadcastSettingsChangeAsync(ChangedPart part, string summary, CancellationToken token)
    {
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var message = new ClhInternalMessage
        {
            Timestamp = now,
            SettingsChanged = new ClhSettingsChanged
            {
                ChangedPart = part.ToString(),
                Summary = summary,
                EventTime = now
            }
        };
        await BroadcastMessageAsync(message, token);
    }

    private async Task BroadcastPluginTelemetryChangedAsync(PluginInfo pluginInfo, CancellationToken token)
    {
        var telemetry = pluginInfo.GetTelemetrySnapshot();
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var message = new ClhInternalMessage
        {
            Timestamp = now,
            PluginTelemetry = new ClhPluginTelemetryChanged
            {
                PluginUuid = telemetry.PluginUuid,
                ReceivedMessageCount = telemetry.ReceivedMessageCount,
                SentMessageCount = telemetry.SentMessageCount,
                ControlRequestCount = telemetry.ControlRequestCount,
                ControlErrorCount = telemetry.ControlErrorCount,
                LastRoundtripMs = telemetry.LastRoundtripMs,
                EventTime = now
            }
        };
        await BroadcastMessageAsync(message, token);
    }

    private async Task SendNotificationAsync(PipeNotificationCommand notification)
    {
        if (string.IsNullOrWhiteSpace(notification.Message))
        {
            throw new InvalidOperationException("Notification message cannot be empty.");
        }

        switch (notification.Level)
        {
            case PipeNotificationLevel.Success:
                await _notificationService.SendSuccessNotificationAsync(notification.Message);
                break;
            case PipeNotificationLevel.Warning:
                await _notificationService.SendWarningNotificationAsync(notification.Message);
                break;
            case PipeNotificationLevel.Error:
                await _notificationService.SendErrorNotificationAsync(notification.Message);
                break;
            case PipeNotificationLevel.Info:
            case PipeNotificationLevel.Unspecified:
            default:
                await _notificationService.SendInfoNotificationAsync(notification.Message);
                break;
        }
    }

    private async Task ExecuteMainWindowVisibilityAsync(bool visible)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = _windowManagerService.GetToplevel(typeof(MainWindowViewModel));
            if (window is null)
            {
                throw new InvalidOperationException("Main window is unavailable.");
            }

            if (visible)
            {
                window.Show();
                window.Activate();
            }
            else
            {
                window.Hide();
            }
        });
    }

    private (bool Success, string Message) ApplySettingsPatch(PipeSettingsPatch? patch)
    {
        if (patch is null || patch.Values.Count == 0)
        {
            return (false, "settings patch is empty");
        }

        if (!_settingsService.TryGetDraftSettings(this, out var draft) || draft is null)
        {
            return (false, "settings are currently locked");
        }

        var errors = new List<string>();
        try
        {
            foreach (var (key, value) in patch.Values)
            {
                ApplyPatchValue(draft, key, value, errors);
            }

            if (errors.Count > 0)
            {
                _settingsService.RestoreSettings(this);
                return (false, string.Join("; ", errors));
            }

            _settingsService.ApplySettings(this);
            return (true, "settings-updated");
        }
        catch (Exception ex)
        {
            try
            {
                _settingsService.RestoreSettings(this);
            }
            catch
            {
                // ignored
            }

            return (false, ex.Message);
        }
    }

    private static void ApplyPatchValue(
        ApplicationSettings draft,
        string key,
        string value,
        List<string> errors)
    {
        switch (key.Trim().ToLowerInvariant())
        {
            case "basic.instance_name":
                draft.BasicSettings.InstanceName = value;
                break;
            case "basic.my_maidenhead_grid":
                draft.BasicSettings.MyMaidenheadGrid = value;
                break;
            case "basic.enable_plugin":
                if (bool.TryParse(value, out var enablePlugin))
                {
                    draft.BasicSettings.EnablePlugin = enablePlugin;
                }
                else
                {
                    errors.Add($"invalid bool value for {key}");
                }
                break;
            case "basic.disable_all_charts":
                if (bool.TryParse(value, out var disableCharts))
                {
                    draft.BasicSettings.DisableAllCharts = disableCharts;
                }
                else
                {
                    errors.Add($"invalid bool value for {key}");
                }
                break;
            case "basic.language":
                if (TryParseLanguage(value, out var language))
                {
                    draft.BasicSettings.LanguageType = language;
                }
                else
                {
                    errors.Add($"invalid language value for {key}");
                }
                break;
            case "cloudlog.auto_qso_upload_enabled":
                if (bool.TryParse(value, out var autoQso))
                {
                    draft.CloudlogSettings.AutoQSOUploadEnabled = autoQso;
                }
                else
                {
                    errors.Add($"invalid bool value for {key}");
                }
                break;
            case "cloudlog.auto_rig_upload_enabled":
                if (bool.TryParse(value, out var autoRig))
                {
                    draft.CloudlogSettings.AutoRigUploadEnabled = autoRig;
                }
                else
                {
                    errors.Add($"invalid bool value for {key}");
                }
                break;
            case "udp.enable_udp_server":
                if (bool.TryParse(value, out var enableUdp))
                {
                    draft.UDPSettings.EnableUDPServer = enableUdp;
                }
                else
                {
                    errors.Add($"invalid bool value for {key}");
                }
                break;
            case "udp.enable_connection_from_outside":
                if (bool.TryParse(value, out var allowOutside))
                {
                    draft.UDPSettings.EnableConnectionFromOutside = allowOutside;
                }
                else
                {
                    errors.Add($"invalid bool value for {key}");
                }
                break;
            case "udp.retry_count":
                draft.UDPSettings.RetryCount = value;
                break;
            case "udp.push_notification_on_qso_made":
                if (bool.TryParse(value, out var notifyQsoMade))
                {
                    draft.UDPSettings.PushNotificationOnQSOMade = notifyQsoMade;
                }
                else
                {
                    errors.Add($"invalid bool value for {key}");
                }
                break;
            case "udp.push_notification_on_qso_uploaded":
                if (bool.TryParse(value, out var notifyQsoUploaded))
                {
                    draft.UDPSettings.PushNotificationOnQSOUploaded = notifyQsoUploaded;
                }
                else
                {
                    errors.Add($"invalid bool value for {key}");
                }
                break;
            default:
                errors.Add($"unsupported patch key: {key}");
                break;
        }
    }

    private static bool TryParseLanguage(string value, out SupportedLanguage language)
    {
        language = SupportedLanguage.NotSpecified;
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "english":
            case "en":
            case "en-us":
                language = SupportedLanguage.English;
                return true;
            case "simplifiedchinese":
            case "zh-hans":
            case "zh-cn":
                language = SupportedLanguage.SimplifiedChinese;
                return true;
            case "traditionalchinese":
            case "zh-hant":
            case "zh-tw":
                language = SupportedLanguage.TraditionalChinese;
                return true;
            case "japanese":
            case "ja":
            case "ja-jp":
                language = SupportedLanguage.Japanese;
                return true;
            default:
                return System.Enum.TryParse(value, true, out language);
        }
    }

    private async Task SendInitialSnapshotsAsync(PluginInfo pluginInfo, CancellationToken token)
    {
        if (!pluginInfo.HasCapability(Capability.EventSubscription) || !pluginInfo.WantsEventSnapshot())
        {
            return;
        }

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var subscription = pluginInfo.GetEventSubscriptionSnapshot();
        var topics = subscription.Topics.Count == 0
            ? new[]
            {
                PipeEnvelopeTopic.EventServerStatus,
                PipeEnvelopeTopic.EventRigStatus,
                PipeEnvelopeTopic.EventUdpStatus,
                PipeEnvelopeTopic.EventQsoQueueStatus,
                PipeEnvelopeTopic.EventSettingsChanged
            }
            : subscription.Topics.ToArray();

        foreach (var topic in topics)
        {
            if (!pluginInfo.ShouldReceiveEventTopic(topic))
            {
                continue;
            }

            IMessage? payload = topic switch
            {
                PipeEnvelopeTopic.EventServerStatus => new ClhServerStatusChanged
                {
                    ClhInstanceId = _basicSettings.InstanceName,
                    ClhVersion = VersionInfo.Version,
                    ConnectedPluginCount = (uint)(await GetConnectedPluginsAsync()).Count,
                    EventTime = now
                },
                PipeEnvelopeTopic.EventRigStatus => new ClhRigServiceStatusChanged
                {
                    BackendService = _rigBackendManager.GetServiceType().ToString(),
                    Endpoint = _rigBackendManager.GetServiceEndpointAddress(),
                    Status = _rigBackendManager.IsServiceRunning() ? ClhServiceRunStatus.Running : ClhServiceRunStatus.Stopped,
                    PollingEnabled = _rigBackendManager.GetPollingAllowed(),
                    PollingIntervalSec = (uint)Math.Max(0, _rigBackendManager.GetPollingInterval()),
                    Message = "snapshot",
                    EventTime = now
                },
                PipeEnvelopeTopic.EventUdpStatus => new ClhUdpServiceStatusChanged
                {
                    BindAddress = _udpServerService.GetUdpBindingAddress(),
                    Status = _udpServerService.IsUdpServerRunning() ? ClhServiceRunStatus.Running : ClhServiceRunStatus.Stopped,
                    AllowExternal = _settingsService.GetCurrentSettings().UDPSettings.EnableConnectionFromOutside,
                    RetryCount = (uint)Math.Max(0, _udpServerService.QSOUploadRetryCount()),
                    NotifyOnQsoMade = _udpServerService.IsNotifyOnQsoMade(),
                    NotifyOnQsoUploaded = _udpServerService.IsNotifyOnQsoUploaded(),
                    Message = "snapshot",
                    EventTime = now
                },
                PipeEnvelopeTopic.EventQsoQueueStatus => new ClhQsoQueueStatusChanged
                {
                    PendingCount = (uint)Math.Max(0, await _qsoUploadService.GetPendingCountAsync()),
                    UploadedTotal = (ulong)Math.Max(0, Interlocked.Read(ref _qsoUploadSuccessTotal)),
                    FailedTotal = (ulong)Math.Max(0, Interlocked.Read(ref _qsoUploadFailTotal)),
                    EventTime = now
                },
                PipeEnvelopeTopic.EventSettingsChanged => new ClhSettingsChanged
                {
                    ChangedPart = "snapshot",
                    Summary = "settings-snapshot",
                    EventTime = now
                },
                _ => null
            };

            if (payload is null)
            {
                continue;
            }

            var envelope = new PipeEnvelope
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = PipeEnvelopeKind.Event,
                Topic = topic,
                Success = true,
                Message = "snapshot",
                Payload = Any.Pack(payload),
                Timestamp = now
            };
            await pluginInfo.SendMessage(envelope, token);
        }
    }

    private async Task TryBroadcastEnvelopeEventAsync(IMessage message, CancellationToken token)
    {
        switch (message)
        {
            case WsjtxMessage wsjtxMessage:
                await BroadcastEnvelopeEventAsync(
                    wsjtxMessage.PayloadCase == WsjtxMessage.PayloadOneofCase.Decode
                        ? PipeEnvelopeTopic.EventWsjtxDecodeRealtime
                        : PipeEnvelopeTopic.EventWsjtxMessage,
                    wsjtxMessage,
                    token);
                break;
            case PackedDecodeMessage decodeBatch:
                await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventWsjtxDecodeBatch, decodeBatch, token);
                break;
            case RigData rigData:
                await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventRigData, rigData, token);
                break;
            case ClhInternalMessage internalMessage:
                switch (internalMessage.PayloadCase)
                {
                    case ClhInternalMessage.PayloadOneofCase.ServerStatus:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventServerStatus, internalMessage.ServerStatus, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.PluginLifecycle:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventPluginLifecycle, internalMessage.PluginLifecycle, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.QsoUploadStatus:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventQsoUploadStatus, internalMessage.QsoUploadStatus, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.RigServiceStatus:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventRigStatus, internalMessage.RigServiceStatus, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.UdpServiceStatus:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventUdpStatus, internalMessage.UdpServiceStatus, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.QsoQueueStatus:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventQsoQueueStatus, internalMessage.QsoQueueStatus, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.SettingsChanged:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventSettingsChanged, internalMessage.SettingsChanged, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.PluginTelemetry:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventPluginTelemetry, internalMessage.PluginTelemetry, token);
                        break;
                }
                break;
        }
    }

    private async Task BroadcastEnvelopeEventAsync(PipeEnvelopeTopic topic, IMessage? payload, CancellationToken token)
    {
        if (payload is null)
        {
            return;
        }

        var envelope = new PipeEnvelope
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = PipeEnvelopeKind.Event,
            Topic = topic,
            Success = true,
            Message = "event",
            Payload = Any.Pack(payload),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        var tasks = new List<Task>();
        using (var readerLock = await _pluginLock.ReaderLockAsync(token))
        {
            foreach (var plugin in _plugins)
            {
                if (!plugin.HasCapability(Capability.EventSubscription))
                {
                    continue;
                }
                tasks.Add(plugin.SendMessage(envelope, token));
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    private static ulong SafeToUlong(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= ulong.MaxValue)
        {
            return ulong.MaxValue;
        }

        return (ulong)value;
    }

    private static uint SafeToUint(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)value;
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

