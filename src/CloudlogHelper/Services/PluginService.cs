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
    
    public string Uuid { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public Capability[] Capabilities { get; set; }
    public string Description { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime LastHeartbeat { get; private set; }

    private NamedPipeServerStream? _client;
    private SemaphoreSlim _cliLock = new(1,1);

    private Task? _heartbeatTask;
    private CancellationTokenSource? _heartbeatCts;
    private bool _disposed = false;

    private async Task ReceiveHeartbeatAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? client;

            await _cliLock.WaitAsync(token);
            try
            {
                if (_client == null || !_client.IsConnected)
                    break;
                client = _client;
            }finally{_cliLock.Release();}

            try
            {
                var hb = await PipeHeartbeat.Parser.ParseDelimitedFromAsync(client, token);

                if (hb?.Uuid == Uuid)
                {
                    ClassLogger.Debug($"Heartbeat received for {Name}");
                    LastHeartbeat = DateTime.UtcNow;
                }
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
                ClassLogger.Error(ex, $"Error receiving heartbeat from {Name}");
                await Task.Delay(1000, token);
            }
        }
    }

    private void StartHeartbeat(CancellationToken token)
    {
        if (_disposed) return;

        _cliLock.Wait(token);
        try
        {
            if (_heartbeatTask != null || _client == null) return;
            _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _heartbeatTask = Task.Run(() => ReceiveHeartbeatAsync(_heartbeatCts.Token), token);
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
            SendMessage(new PipeConnectionClosed()
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // ignored
        }
        
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;

        _cliLock.Wait();
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
        _cliLock.Release();
    }

    public static PluginInfo Create(PipeRegisterPluginReq rpcRegisterPluginReq,
        NamedPipeServerStream client, CancellationToken token)
    {
        var pg = new PluginInfo
        {
            Uuid = rpcRegisterPluginReq.Uuid,
            Name = rpcRegisterPluginReq.Name,
            Version = rpcRegisterPluginReq.Version,
            Capabilities = rpcRegisterPluginReq.Capabilities.ToArray(),
            Description = rpcRegisterPluginReq.Description,
            _client = client,
            RegisteredAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow
        };

        pg.StartHeartbeat(token);
        return pg;
    }

    public async Task SendMessage<T>(T msg, CancellationToken token) where T: IMessage
    {
        switch (msg)
        {
            case PackedDecodeMessage:
            case WsjtxMessage:
                if (!Capabilities.Contains(Capability.WsjtxMessage))return ;
                break;
            case RigData:
                if (!Capabilities.Contains(Capability.RigData))return ;
                break;
            case ClhInternalMessage:
                if (!Capabilities.Contains(Capability.ClhInternalData))return ;
                break;
            case PipeConnectionClosed:
                break;
            default:
                return ;
        }

        var any = Any.Pack(msg);

        await _cliLock.WaitAsync(token);
        try
        {
            if (_client is null || !_client.IsConnected) return;
            await any.WriteDelimitedToAsync(_client, token);
        }
        finally
        {
            _cliLock.Release();
        }
    }

    public void Dispose()
    {
        StopAll();
    }
}

public class PluginService: IPluginService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private List<PluginInfo> _plugins = new();
    private readonly AsyncReaderWriterLock _pluginLock = new();

    private CancellationTokenSource? _source;
    private Task? _pluginTask;
    private readonly object _serviceLock = new();
    private bool _isRunning;
    
    private readonly ObservableCollection<Decode> _wsjtxDecodeCache = new();
    
    private readonly BasicSettings _basicSettings;
    private readonly CompositeDisposable _settingsSubscription = new();
    
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObservableCollection<WsjtxMessage>))]
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

    private async Task StartPluginServiceAsync()
    {
        lock (_serviceLock)
        {
            if (_isRunning) return;
            
            try
            {
                _source = new CancellationTokenSource();
                var tk = _source.Token;
                _pluginTask = Task.Run(()=>_startService(tk), tk);
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
        
        if (!_isRunning) return; // Check if service is running

        // throttle decode; will be sent later.
        if (message is WsjtxMessage { PayloadCase: WsjtxMessage.PayloadOneofCase.Decode } wmsg)
        {
            _wsjtxDecodeCache.Add(wmsg.Decode);
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
            link.Token.Register(() =>
            {
                // force quit waiting
                if (!transferred) server.Dispose();
            });

            var pluginRegisterInfo = await PipeRegisterPluginReq.Parser.ParseDelimitedFromAsync(server, link.Token);
            if (pluginRegisterInfo is null)
            {
                throw new Exception("Plugin register info is null");
            }
            ClassLogger.Trace($"Got plugin: {pluginRegisterInfo.Name}");
            await _sendResponse(server, true, null, link.Token);

            var pluginInfo = PluginInfo.Create(pluginRegisterInfo, server, cancellationToken);
            ownershipTransferred = true;

            using var writerLock = await _pluginLock.WriterLockAsync(cancellationToken);
            var dupePlugin = _plugins.Where(x => x.Uuid == pluginInfo.Uuid).ToList();
            foreach (var info in dupePlugin)
            {
                ClassLogger.Debug("removing repeated plugin : " + info.Name);
                _plugins.Remove(info);
                info.Dispose();
            }

            _plugins.Add(pluginInfo);
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Parsing plugin register info failed");
            if (cancellationToken.IsCancellationRequested)
            {
                ClassLogger.Info("ProcessPluginRegistration stopped");
                return;
            }
            await _sendResponse(server, false, e.Message, cancellationToken);
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

    private async Task _sendResponse(NamedPipeServerStream server, bool success, string? message,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = new PipeRegisterPluginResp
            {
                Success = success,
                ClhInstanceId = _basicSettings.InstanceName,
                Message = message ?? string.Empty,
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
                    ClassLogger.Trace("Nothing to remove.");
                    continue;
                }

                using var writerLock = await _pluginLock.WriterLockAsync(cancellationToken);
                ClassLogger.Debug($"Removing {string.Join(",", toRemove.Select(p => p.Name))}");
                foreach (var plugin in toRemove)
                {
                    _plugins.Remove(plugin);
                    plugin.Dispose();
                }
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