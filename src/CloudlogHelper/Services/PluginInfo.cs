using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using NLog;
using SydneyOwl.CLHProto.Plugin;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Services;

public class PluginInfo : IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly Func<PluginInfo, PipeEnvelope, CancellationToken, Task> _envelopeHandler;
    private readonly Func<string, string, Task> _selfDeregister;
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

    private PipeEnvelopeTopic[]? _eventTopics;

    public string Uuid { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string SdkName { get; private set; } = string.Empty;
    public string SdkVersion { get; private set; } = string.Empty;
    public DateTime RegisteredAt { get; private set; }
    public DateTime LastHeartbeat => new(Interlocked.Read(ref _lastHeartbeatTicks), DateTimeKind.Utc);
    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    private PluginInfo(
        Func<PluginInfo, PipeEnvelope, CancellationToken, Task> envelopeHandler,
        Func<string, string, Task> selfDeregister)
    {
        _envelopeHandler = envelopeHandler;
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
                    await _selfDeregister(Uuid, "disconnected from plugin pipe (nil message recv)");
                    break;
                }

                IncrementReceived();

                if (anyMessage.Is(PipeDeregisterPluginReq.Descriptor))
                {
                    var dereg = anyMessage.Unpack<PipeDeregisterPluginReq>();
                    try
                    {
                        await SendMessage(new PipeDeregisterPluginResp
                        {
                            Success = true,
                            Message = null,
                            Timestamp = new Timestamp()
                        }, token);
                        ClassLogger.Info($"{Name} exited normally.");
                    }
                    catch
                    {
                        // ...
                    }

                    await _selfDeregister(Uuid,
                        string.IsNullOrWhiteSpace(dereg.Reason) ? "Plugin exited normally." : dereg.Reason);
                    break;
                }

                if (anyMessage.Is(PipeHeartbeat.Descriptor))
                {
                    var heartbeat = anyMessage.Unpack<PipeHeartbeat>();
                    if (!string.IsNullOrWhiteSpace(heartbeat.Uuid) && heartbeat.Uuid != Uuid)
                        ClassLogger.Warn(
                            $"Heartbeat uuid mismatch for plugin {Name}. Expected {Uuid}, got {heartbeat.Uuid}");

                    UpdateHeartbeat();
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

                ClassLogger.Warn($"Unknown plugin message received from {Name}: {anyMessage.TypeUrl}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException) when (!token.IsCancellationRequested)
            {
                ClassLogger.Info($"{Name} exited due to IO exception");
                await _selfDeregister(Uuid, "exited due to IO exception");
                break;
            }
            catch (Exception ex)
            {
                ClassLogger.Error(ex, $"Error receiving message from plugin {Name}");
                // await Task.Delay(500, token);
                await _selfDeregister(Uuid, $"Error receiving message: {ex.Message}");
                break;
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

    private void StopAll(bool notify)
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            if (notify)
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
        finally
        {
            _cliLock.Release();
        }
    }

    public static PluginInfo Create(
        PipeRegisterPluginReq rpcRegisterPluginReq,
        NamedPipeServerStream client,
        CancellationToken token,
        Func<PluginInfo, PipeEnvelope, CancellationToken, Task> envelopeHandler,
        Func<string, string, Task> selfDeregister)
    {
        var pg = new PluginInfo(envelopeHandler, selfDeregister)
        {
            Uuid = rpcRegisterPluginReq.Uuid,
            Name = rpcRegisterPluginReq.Name,
            Version = rpcRegisterPluginReq.Version,
            Description = rpcRegisterPluginReq.Description,
            SdkName = rpcRegisterPluginReq.SdkName,
            SdkVersion = rpcRegisterPluginReq.SdkVersion,
            _client = client,
            RegisteredAt = DateTime.UtcNow
        };

        foreach (var (key, value) in rpcRegisterPluginReq.Metadata)
            pg._metadata[key] = value;

        pg.UpdateEventSubscription(rpcRegisterPluginReq.EventSubscription);
        pg.UpdateHeartbeat();
        pg.StartMessageLoop(token);
        return pg;
    }

    public void UpdateEventSubscription(PipeEventSubscription? subscription)
    {
        if (subscription is null) return;
        lock (_subscriptionLock)
        {
            _eventTopics = subscription.Topics.Distinct().ToArray();
        }
    }

    public PipeEventSubscription GetEventSubscriptionSnapshot()
    {
        var snapshot = new PipeEventSubscription();
        lock (_subscriptionLock)
        {
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
            EventSubscription = GetEventSubscriptionSnapshot(),
            Telemetry = GetTelemetrySnapshot()
        };

        foreach (var (key, value) in _metadata)
            info.Metadata[key] = value;

        return info;
    }

    public async Task SendMessage<T>(T msg, CancellationToken token) where T : IMessage
    {
        switch (msg)
        {
            case PipeEnvelope envelope:
                if (envelope.Kind == PipeEnvelopeKind.Event)
                    if (!ShouldReceiveEventTopic(envelope.Topic))
                        return;
                break;
            case PipeConnectionClosed:
                break;
            case PipeDeregisterPluginResp:
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
            ClassLogger.Debug(ex, $"[ignored] Failed to send message to plugin {Name}");
        }
    }

    public void Dispose()
    {
        Task.Run(() => StopAll(true)).GetAwaiter().GetResult();
    }

    public void DisposeWithoutNotify()
    {
        Task.Run(() => StopAll(false)).GetAwaiter().GetResult();
    }
}
