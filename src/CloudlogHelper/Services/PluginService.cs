using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdifParser;
using Avalonia.Threading;
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
using Enum = System.Enum;
using UploadStatus = CloudlogHelper.Enums.UploadStatus;

namespace CloudlogHelper.Services;


public class PluginService : IPluginService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly List<PluginInfo> _plugins = new();
    private readonly AsyncReaderWriterLock _pluginLock = new();

    private readonly IApplicationSettingsService _settingsService;
    private readonly IRigBackendManager _rigBackendManager;
    private readonly IUdpServerService _udpServerService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly IInAppNotificationService _notificationService;
    private readonly IQsoQueueStore _qsoQueueStore;
    private readonly IQSOUploadService _qsoUploadService;

    private CancellationTokenSource? _source;
    private Task? _pluginTask;
    private readonly object _serviceLock = new();
    private bool _isRunning;
    private DateTime _serviceStartedAt = DateTime.UtcNow;

    private readonly ObservableCollection<Decode> _wsjtxDecodeCache = new();
    private readonly object _decodeCacheLock = new();

    private readonly BasicSettings _basicSettings;
    private readonly CompositeDisposable _disp = new();

    public PluginService(
        IApplicationSettingsService settingsService,
        IRigBackendManager rigBackendManager,
        IUdpServerService udpServerService,
        IWindowManagerService windowManagerService,
        IInAppNotificationService notificationService,
        IQsoQueueStore qsoQueueStore,
        IQSOUploadService qsoUploadService)
    {
        _settingsService = settingsService;
        _rigBackendManager = rigBackendManager;
        _udpServerService = udpServerService;
        _windowManagerService = windowManagerService;
        _notificationService = notificationService;
        _qsoQueueStore = qsoQueueStore;
        _basicSettings = settingsService.GetCurrentSettings().BasicSettings;
        _qsoUploadService = qsoUploadService;

        // high frequency msgs like deocded
        MessageBus.Current.RegisterScheduler<PluginEvent>(RxSchedulers.TaskpoolScheduler);

        // batch send decode
        _disp.Add(
            _wsjtxDecodeCache.ObserveCollectionChanges()
                .Throttle(TimeSpan.FromSeconds(2))
                .ObserveOn(RxSchedulers.TaskpoolScheduler)
                .Subscribe(changes => HandleDecodeCacheChanged()
                    .SafeFireAndForget(nameof(HandleDecodeCacheChanged)))
        );

        // push settings'changed
        _disp.Add(MessageBus.Current
            .Listen<SettingsChanged>()
            .Subscribe(x => HandleSettingsChange(x)
                .SafeFireAndForget(nameof(HandleSettingsChange))));

        // receive IMessage
        _disp.Add(
            MessageBus.Current
                .Listen<PluginEvent>()
                .Subscribe(x =>
                {
                    if (x.Message is null) return;
                    BroadcastMessageAsync(x.Message, CancellationToken.None)
                        .SafeFireAndForget(nameof(BroadcastMessageAsync));
                }));

        if (_basicSettings.EnablePlugin) StartPluginServiceAsync().SafeFireAndForget(nameof(StartPluginServiceAsync));
    }

    private async Task HandleDecodeCacheChanged()
    {
        if (!_isRunning) return;

        Decode[] decodes;
        lock (_decodeCacheLock)
        {
            decodes = _wsjtxDecodeCache.ToArray();
            if (decodes.Length == 0) return;
            _wsjtxDecodeCache.Clear();
        }

        ClassLogger.Trace("Sending throttled decoded message.");
        var packedMessage = new PackedDecodeMessage();
        packedMessage.Messages.AddRange(decodes);
        packedMessage.Timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        await BroadcastMessageAsync(packedMessage, CancellationToken.None);
    }

    private async Task HandleSettingsChange(SettingsChanged changed)
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

        if (_pluginTask != null)
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

        using (var writerLock = await _pluginLock.WriterLockAsync(CancellationToken.None))
        {
            pluginsToDispose = new List<PluginInfo>(_plugins);
            _plugins.Clear();
        }


        foreach (var plugin in pluginsToDispose)
            try
            {
                plugin.Dispose();
            }
            catch (Exception ex)
            {
                ClassLogger.Error(ex, $"Error disposing plugin {plugin.Name}");
            }

        lock (_decodeCacheLock)
        {
            _wsjtxDecodeCache.Clear();
        }

        ClassLogger.Info("Plugin service stopped");
    }

    public void Dispose()
    {
        _disp?.Dispose();

        if (_isRunning) Task.Run(StopPluginServiceAsync).GetAwaiter().GetResult();
    }

    public async Task BroadcastMessageAsync(IMessage? message, CancellationToken token)
    {
        if (message is null) return;

        if (!_isRunning) return;

        // called directly from udp server
        if (message is WsjtxMessage { PayloadCase: WsjtxMessage.PayloadOneofCase.Decode } wsjtxDecodeMessage)
        {
            var hasBatchedDecodeSubscriber = false;
            using (var readerLock = await _pluginLock.ReaderLockAsync(token))
            {
                foreach (var pluginInfo in _plugins)
                {
                    if (!pluginInfo.ShouldReceiveEventTopic(PipeEnvelopeTopic.EventWsjtxDecodeBatch)) continue;
                    hasBatchedDecodeSubscriber = true;
                    break;
                }
            }

            if (hasBatchedDecodeSubscriber && wsjtxDecodeMessage.Decode is not null)
                lock (_decodeCacheLock)
                {
                    _wsjtxDecodeCache.Add(wsjtxDecodeMessage.Decode);
                }
        }

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

    public async Task<bool> DisconnectPluginAsync(string pluginUuid, string reason, CancellationToken token,
        bool notify = true)
    {
        if (string.IsNullOrWhiteSpace(pluginUuid)) return false;

        PluginInfo? target = null;
        using (var writerLock = await _pluginLock.WriterLockAsync(token))
        {
            target = _plugins.FirstOrDefault(x => x.Uuid == pluginUuid);
            if (target != null) _plugins.Remove(target);
        }

        if (target == null) return false;

        if (notify)
            target.Dispose();
        else
            target.DisposeWithoutNotify();
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
                if (pipeServer is not null) await pipeServer.DisposeAsync();
                ClassLogger.Info("Plugin reg service stopped");
                break;
            }
            catch (Exception e)
            {
                if (pipeServer is not null) await pipeServer.DisposeAsync();
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

            using var reg = link.Token.Register(() =>
            {
                // ReSharper disable once AccessToModifiedClosure
                if (!ownershipTransferred) server.Dispose();
            });

            var pluginRegisterInfo = await PipeRegisterPluginReq.Parser.ParseDelimitedFromAsync(server, link.Token);
            if (pluginRegisterInfo is null) throw new Exception("Plugin register info is null");

            ClassLogger.Trace($"Plugin register request received: {pluginRegisterInfo.Name}");
            var registerServerInfo = await BuildServerInfoAsync(link.Token);
            registerServerInfo.ConnectedPluginCount += 1;
            await _sendResponse(server, true, null, registerServerInfo, link.Token);

            var pluginInfo = PluginInfo.Create(
                pluginRegisterInfo,
                server,
                cancellationToken,
                HandleEnvelopeRequestAsync,
                (uuid, reason) => DisconnectPluginAsync(uuid, reason, CancellationToken.None, false));
            ownershipTransferred = true;

            List<PluginInfo> replacedPlugins;
            using (var writerLock = await _pluginLock.WriterLockAsync(cancellationToken))
            {
                replacedPlugins = _plugins.Where(x => x.Uuid == pluginInfo.Uuid).ToList();
                foreach (var replaced in replacedPlugins) _plugins.Remove(replaced);
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

    private async Task HandleEnvelopeRequestAsync(PluginInfo pluginInfo, PipeEnvelope envelope, CancellationToken token)
    {
        if (envelope.Kind is not (PipeEnvelopeKind.Query or PipeEnvelopeKind.Command)) return;

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
                case PipeEnvelopeTopic.QuerySettingsSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(BuildSettingsSnapshot());
                    break;
                case PipeEnvelopeTopic.QueryQsoQueueSnapshot:
                    response.Success = true;
                    response.Message = "ok";
                    response.Payload = Any.Pack(await BuildQSOQueueSnapshotAsync(token));
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
                case PipeEnvelopeTopic.CommandSubscribeEvents:
                {
                    var subscription = envelope.Subscription;
                    if (subscription is null && envelope.Payload != null &&
                        envelope.Payload.Is(PipeEventSubscription.Descriptor))
                        subscription = envelope.Payload.Unpack<PipeEventSubscription>();

                    if (subscription is null)
                    {
                        response.Success = false;
                        response.ErrorCode = "invalid_payload";
                        response.Message = "PipeEventSubscription payload is required";
                        break;
                    }

                    pluginInfo.UpdateEventSubscription(subscription);
                    response.Success = true;
                    response.Message = "event-subscription-updated";
                    response.Payload = Any.Pack(pluginInfo.GetEventSubscriptionSnapshot());
                    break;
                }
                case PipeEnvelopeTopic.CommandShowMainWindow:
                    response.Success = false;
                    response.Message = "not-supported";
                    break;
                case PipeEnvelopeTopic.CommandHideMainWindow:
                    response.Success = false;
                    response.Message = "not-supported";
                    break;
                case PipeEnvelopeTopic.CommandOpenWindow:
                    if (!envelope.Attributes.TryGetValue("window", out var window)) break;
                    if (!envelope.Attributes.TryGetValue("asDialog", out var dialog)) break;

                    if (string.IsNullOrEmpty(window)) break;
                    if (string.IsNullOrEmpty(dialog)) break;

                    var tryParse = Enum.TryParse<PluginControllableWindow>(window, out var pCtWindow);
                    if (!tryParse) break;

                    Dispatcher.UIThread.InvokeAsync(async () =>
                            await _windowManagerService.CreateAndShowWindowByVm(pCtWindow, null, dialog == "true"))
                        .SafeFireAndForget(nameof(_windowManagerService.CreateAndShowWindowByVm));

                    response.Success = true;
                    response.Message = "window-opened";
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
                        enabled = parsedEnabled;

                    var patch = new PipeSettingsPatch();
                    patch.Values["udp.enable_udp_server"] = enabled.ToString();
                    var patchResult = ApplySettingsPatch(patch);
                    response.Success = patchResult.Success;
                    response.Message = patchResult.Message;
                    if (response.Success) response.Payload = Any.Pack(await BuildUdpSnapshotAsync(token));
                    break;
                }
                case PipeEnvelopeTopic.CommandToggleRigBackend:
                    var rigEnabled = !_rigBackendManager.GetPollingAllowed();
                    var defaultBackend = _rigBackendManager.GetServiceType();

                    if (envelope.Attributes.TryGetValue("enabled", out var enableRigRaw) &&
                        bool.TryParse(enableRigRaw, out var parsedRigEnabled))
                        rigEnabled = parsedRigEnabled;

                    var rigPatch = new PipeSettingsPatch();
                    rigPatch.Values["rig.enable_polling"] = $"{rigEnabled};;;{defaultBackend}";
                    var rigPatchResult = ApplySettingsPatch(rigPatch);

                    response.Success = rigPatchResult.Success;
                    response.Message = rigPatchResult.Message;
                    if (response.Success) response.Payload = Any.Pack(await BuildRigSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.CommandSwitchRigBackend:
                    if (!envelope.Attributes.TryGetValue("backend", out var rigBackend) ||
                        !Enum.TryParse<RigBackendServiceEnum>(rigBackend, out _))
                    {
                        response.Success = false;
                        response.Message = "Invalid backend";
                        break;
                    }

                    var rigBackendPatch = new PipeSettingsPatch();
                    rigBackendPatch.Values["rig.backend"] = rigBackend;
                    var rigBackendPatchResult = ApplySettingsPatch(rigBackendPatch);

                    response.Success = rigBackendPatchResult.Success;
                    response.Message = rigBackendPatchResult.Message;
                    if (response.Success) response.Payload = Any.Pack(await BuildRigSnapshotAsync(token));
                    break;
                case PipeEnvelopeTopic.CommandTriggerQsoReupload:
                    if (!envelope.Attributes.TryGetValue("qsoIds", out var qsoIds)) break;

                    var qsoIdList = qsoIds.Split(";;;").Where(x => !string.IsNullOrWhiteSpace(x));

                    var callDetails = new List<RecordedCallsignDetail>();

                    foreach (var se in qsoIdList)
                    {
                        var recordedCallsignDetail = _qsoQueueStore.Items.FirstOrDefault(x => x.Uuid == se);
                        if (recordedCallsignDetail is null)
                        {
                            response.Success = false;
                            response.Message = $"QSO {se} not found";
                            break;
                        }

                        callDetails.Add(recordedCallsignDetail);
                    }

                    foreach (var recordedCallsignDetail in callDetails)
                    {
                        if (recordedCallsignDetail.UploadStatus != UploadStatus.Fail &&
                            recordedCallsignDetail.UploadStatus != UploadStatus.Ignored)
                        {
                            response.Success = false;
                            response.Message =
                                $"QSO {recordedCallsignDetail.Uuid} is being processed. Reupload is ignored";
                            break;
                        }

                        recordedCallsignDetail.ForcedUpload = true;
                        await _qsoUploadService.EnqueueQSOForUploadAsync(recordedCallsignDetail, token);
                    }

                    response.Success = true;
                    break;
                case PipeEnvelopeTopic.CommandUploadExternalQso:
                    if (!envelope.Attributes.TryGetValue("adifLogs", out var adif)) break;
                    if (string.IsNullOrEmpty(adif)) break;
                    var localParser = new AdifDocument();
                    localParser.ReadFromString(adif, token);
                    if (localParser.Qsos.Count == 0)
                    {
                        response.Success = false;
                        response.Message = "Failed to parse QSO";
                        break;
                    }

                    var cloudParsed = localParser.Qsos
                        .AsParallel()
                        .WithCancellation(token)
                        .Select(x => RecordedCallsignDetail.Parse(AdifLog.Parse(x), DXCCKeys.externallog))
                        .ToList();

                    MessageBus.Current.SendMessage(new QsoUploadRequested
                    {
                        QsoData = cloudParsed
                    });
                    response.Success = true;
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
                        foreach (var kv in envelope.Attributes) patch.Values[kv.Key] = kv.Value;
                    }

                    var patchResult = ApplySettingsPatch(patch);
                    response.Success = patchResult.Success;
                    response.Message = patchResult.Message;
                    if (response.Success) response.Payload = Any.Pack(BuildSettingsSnapshot());
                    break;
                }
                default:
                    response.Success = false;
                    response.ErrorCode = "unsupported_topic";
                    response.Message = $"Unsupported topic: {envelope.Topic}";
                    break;
            }

            if (!response.Success) pluginInfo.IncrementControlError();
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
            foreach (var plugin in _plugins) list.Plugins.Add(plugin.ToPipePluginInfo());
        }

        return list;
    }

    private async Task<PipeRuntimeSnapshot> BuildRuntimeSnapshotAsync(CancellationToken token)
    {
        var serverTask = BuildServerInfoAsync(token);
        var rigTask = BuildRigSnapshotAsync(token);
        var udpTask = BuildUdpSnapshotAsync(token);

        await Task.WhenAll(serverTask, rigTask, udpTask);

        var snapshot = new PipeRuntimeSnapshot
        {
            ServerInfo = await serverTask,
            RigSnapshot = await rigTask,
            UdpSnapshot = await udpTask,
            SettingsSnapshot = BuildSettingsSnapshot(),
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        using (var readerLock = await _pluginLock.ReaderLockAsync(token))
        {
            foreach (var plugin in _plugins) snapshot.PluginTelemetry.Add(plugin.GetTelemetrySnapshot());
        }

        return snapshot;
    }

    private async Task<PipeRigStatusSnapshot> BuildRigSnapshotAsync(CancellationToken token)
    {
        var snapshot = new PipeRigStatusSnapshot
        {
            Provider = _rigBackendManager.GetServiceType().ToString(),
            Endpoint = _rigBackendManager.GetServiceEndpointAddress(),
            ServiceRunning = _rigBackendManager.IsServiceRunning(),
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        try
        {
            var data = await _rigBackendManager.GetAllRigInfo();
            snapshot.RigModel = data.RigName;
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

    private Task<PipeUdpStatusSnapshot> BuildUdpSnapshotAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settings = _settingsService.GetCurrentSettings().UDPSettings;
        return Task.FromResult(new PipeUdpStatusSnapshot
        {
            ServerRunning = _udpServerService.IsUdpServerRunning(),
            BindAddress = _udpServerService.GetUdpBindingAddress(),
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    private Task<PipeQsoQueueSnapshot> BuildQSOQueueSnapshotAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var msgs = _qsoQueueStore.Items.Select(PbMsgConverter.ToPbRecordedQSODetail);
        var res = new PipeQsoQueueSnapshot
        {
            SampledAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        res.Details.AddRange(msgs);
        return Task.FromResult(res);
    }

    private PipeMainSettingsSnapshot BuildSettingsSnapshot()
    {
        var settings = _settingsService.GetCurrentSettings();
        return new PipeMainSettingsSnapshot
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
            return requester.GetTelemetrySnapshot();

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

    private async Task SendNotificationAsync(PipeNotificationCommand notification)
    {
        if (string.IsNullOrWhiteSpace(notification.Message))
            throw new InvalidOperationException("Notification message cannot be empty.");

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

    private (bool Success, string Message) ApplySettingsPatch(PipeSettingsPatch? patch)
    {
        if (patch is null || patch.Values.Count == 0) return (false, "settings patch is empty");

        if (!_settingsService.TryGetDraftSettings(this, out var draft) || draft is null)
            return (false, "settings are currently locked");

        var errors = new List<string>();
        try
        {
            foreach (var (key, value) in patch.Values) ApplyPatchValue(draft, key, value, errors);

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
                ApplyBoolPatchValue(key, value, v => draft.BasicSettings.EnablePlugin = v, errors);
                break;
            case "basic.disable_all_charts":
                ApplyBoolPatchValue(key, value, v => draft.BasicSettings.DisableAllCharts = v, errors);
                break;
            case "cloudlog.auto_qso_upload_enabled":
                ApplyBoolPatchValue(key, value, v => draft.CloudlogSettings.AutoQSOUploadEnabled = v, errors);
                break;
            case "cloudlog.auto_rig_upload_enabled":
                ApplyBoolPatchValue(key, value, v => draft.CloudlogSettings.AutoRigUploadEnabled = v, errors);
                break;
            case "udp.enable_udp_server":
                ApplyBoolPatchValue(key, value, v => draft.UDPSettings.EnableUDPServer = v, errors);
                break;
            case "udp.enable_connection_from_outside":
                ApplyBoolPatchValue(key, value, v => draft.UDPSettings.EnableConnectionFromOutside = v, errors);
                break;
            case "udp.retry_count":
                draft.UDPSettings.RetryCount = value;
                break;
            case "udp.push_notification_on_qso_made":
                ApplyBoolPatchValue(key, value, v => draft.UDPSettings.PushNotificationOnQSOMade = v, errors);
                break;
            case "udp.push_notification_on_qso_uploaded":
                ApplyBoolPatchValue(key, value, v => draft.UDPSettings.PushNotificationOnQSOUploaded = v, errors);
                break;
            case "rig.enable_polling":
                var raw = value.Split(";;;");
                if (raw.Length != 2)
                {
                    errors.Add("Invalid command");
                    break;
                }

                if (Enum.TryParse<RigBackendServiceEnum>(raw[1], out var backendService) &&
                    bool.TryParse(raw[0], out var enablePolling))
                {
                    SetRigPollingPatch(draft, backendService, enablePolling, errors);
                }
                else
                {
                    errors.Add($"invalid rig value for {key}");
                }

                break;
            case "rig.backend":
                if (Enum.TryParse<RigBackendServiceEnum>(value, out var rBackend))
                {
                    SetRigPollingPatch(draft, rBackend, true, errors);
                }
                else
                {
                    errors.Add($"invalid rig value for {key}");
                }

                break;
            default:
                errors.Add($"unsupported patch key: {key}");
                break;
        }
    }

    private static void ApplyBoolPatchValue(
        string key,
        string value,
        Action<bool> setter,
        List<string> errors)
    {
        if (bool.TryParse(value, out var parsed))
            setter(parsed);
        else
            errors.Add($"invalid bool value for {key}");
    }

    private static void SetRigPollingPatch(
        ApplicationSettings draft,
        RigBackendServiceEnum backend,
        bool enablePolling,
        List<string> errors)
    {
        draft.HamlibSettings.PollAllowed = false;
        draft.FLRigSettings.PollAllowed = false;
        draft.OmniRigSettings.PollAllowed = false;

        switch (backend)
        {
            case RigBackendServiceEnum.Hamlib:
                draft.HamlibSettings.PollAllowed = enablePolling;
                break;
            case RigBackendServiceEnum.FLRig:
                draft.FLRigSettings.PollAllowed = enablePolling;
                break;
            case RigBackendServiceEnum.OmniRig:
                draft.OmniRigSettings.PollAllowed = enablePolling;
                break;
            default:
                errors.Add($"unsupported backend: {backend}");
                break;
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
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventServerStatus,
                            internalMessage.ServerStatus, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.PluginLifecycle:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventPluginLifecycle,
                            internalMessage.PluginLifecycle, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.QsoUploadStatus:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventQsoUploadStatus,
                            internalMessage.QsoUploadStatus, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.QsoQueueStatus:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventQsoQueueStatus,
                            internalMessage.QsoQueueStatus, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.SettingsChanged:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventSettingsChanged,
                            internalMessage.SettingsChanged, token);
                        break;
                    case ClhInternalMessage.PayloadOneofCase.PluginTelemetry:
                        await BroadcastEnvelopeEventAsync(PipeEnvelopeTopic.EventPluginTelemetry,
                            internalMessage.PluginTelemetry, token);
                        break;
                }

                break;
        }
    }

    private async Task BroadcastEnvelopeEventAsync(PipeEnvelopeTopic topic, IMessage? payload, CancellationToken token)
    {
        if (payload is null) return;

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
            foreach (var plugin in _plugins) tasks.Add(plugin.SendMessage(envelope, token));
        }

        if (tasks.Count > 0) await Task.WhenAll(tasks);
    }

    private static ulong SafeToUlong(double value)
    {
        if (value <= 0) return 0;

        if (value >= ulong.MaxValue) return ulong.MaxValue;

        return (ulong)value;
    }

    private static uint SafeToUint(double value)
    {
        if (value <= 0) return 0;

        if (value >= uint.MaxValue) return uint.MaxValue;

        return (uint)value;
    }

    private async Task _checkHeartBeat(CancellationToken cancellationToken)
    {
        ClassLogger.Debug("Starting plugin keepalive service");
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                ClassLogger.Trace("Checking heartbeat...");
                var toRemove = new List<PluginInfo>();

                using (var readerLock = await _pluginLock.ReaderLockAsync(cancellationToken))
                {
                    foreach (var pluginInfo in _plugins)
                        if (DateTime.UtcNow - pluginInfo.LastHeartbeat >
                            TimeSpan.FromSeconds(DefaultConfigs.PluginKeepaliveTimeoutSec))
                        {
                            ClassLogger.Info($"{pluginInfo.Name} timeout.");
                            toRemove.Add(pluginInfo);
                        }
                }

                if (toRemove.Count == 0)
                {
                    ClassLogger.Trace("No plugin timeout detected.");
                    continue;
                }

                using (var writerLock = await _pluginLock.WriterLockAsync(cancellationToken))
                {
                    foreach (var plugin in toRemove) _plugins.Remove(plugin);
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
