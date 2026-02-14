using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Nito.AsyncEx;
using NLog;
using SydneyOwl.CLHProto.Plugin;

namespace CloudlogHelper.Services;

internal class PluginInfo : IDisposable
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
    private readonly object _clientLock = new();

    private Task? _heartbeatTask;
    private CancellationTokenSource? _heartbeatCts;
    private bool _disposed = false;

    private async Task ReceiveHeartbeatAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? client;
                lock (_clientLock)
                {
                    if (_client == null || !_client.IsConnected)
                        break;
                    client = _client;
                }

                try
                {
                    var hb = await Task.Run(() =>
                        PipeHeartbeat.Parser.ParseDelimitedFrom(client), token);

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
        finally
        {
            StopAll();
        }
    }

    private void StartHeartbeat(CancellationToken token)
    {
        if (_disposed) return;
        lock (_clientLock)
        {
            if (_heartbeatTask != null || _client == null) return;
            _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _heartbeatTask = Task.Run(() => ReceiveHeartbeatAsync(_heartbeatCts.Token), token);
        }
    }

    private void StopAll()
    {
        if (_disposed) return;
        _disposed = true;

        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;

        lock (_clientLock)
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

    public void Dispose()
    {
        StopAll();
    }
}

public class PluginService: IPluginService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private List<PluginInfo> _plugins = new();
    private string _instanceId;
    private readonly AsyncReaderWriterLock _pluginLock = new();

    private CancellationTokenSource _source;
    private Task? _pluginTask;

    public PluginService(IApplicationSettingsService service)
    {
        _source = new CancellationTokenSource();
        _instanceId = service.GetCurrentSettings().InstanceName;
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
            try
            {
                var server = new NamedPipeServerStream(
                    pipePath,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096, 4096);

                await server.WaitForConnectionAsync(cancellationToken);
                await ProcessPluginRegistration(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                ClassLogger.Error(e, "Plugin registration server error");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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

            var pluginRegisterInfo = PipeRegisterPluginReq.Parser.ParseDelimitedFrom(server);
            ClassLogger.Trace($"Got plugin: {pluginRegisterInfo.Name}");
            await _sendResponse(server, true, null, link.Token);

            var pluginInfo = PluginInfo.Create(pluginRegisterInfo, server, cancellationToken);
            ownershipTransferred = true;

            using var writerLock = await _pluginLock.WriterLockAsync(cancellationToken);
            var dupePlugin = _plugins.Where(x => x.Uuid == pluginInfo.Uuid).ToList();
            foreach (var info in dupePlugin)
            {
                _plugins.Remove(info);
                info.Dispose();
            }

            _plugins.Add(pluginInfo);
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Parsing plugin register info failed");
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
                ClhInstanceId = _instanceId,
                Message = message ?? string.Empty,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            response.WriteDelimitedTo(server);
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
                ClassLogger.Debug($"Removing {string.Join(",", toRemove)}");
                foreach (var plugin in toRemove)
                {
                    _plugins.Remove(plugin);
                    plugin.Dispose();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                ClassLogger.Error(e, "Error in heartbeat check");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        _source.Cancel();
        _pluginTask?.Wait(TimeSpan.FromSeconds(1));
        
        _source.Dispose();
        _pluginTask?.Dispose();
    }

    public Task InitPluginServicesAsync(CancellationToken token)
    {
        _pluginTask = _startService(_source.Token);
        return Task.CompletedTask;
    }
}