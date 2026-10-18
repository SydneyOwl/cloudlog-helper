using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using DynamicData;
using NLog;
using ReactiveUI;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services;

public class ClientRegistryService : IClientRegistryService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly SourceCache<ClientSession, string> _cache = new(x => x.Id);
    private readonly CompositeDisposable _compDisp = new();

    // Touch is called from the UDP receive thread while expiry runs on a pool thread.
    private readonly object _sync = new();

    private bool _disposed;

    public ClientRegistryService()
    {
        Observable.Interval(TimeSpan.FromSeconds(1))
            .ObserveOn(RxSchedulers.TaskpoolScheduler)
            .Subscribe(_ => _expireStaleSessions(),
                ex => ClassLogger.Error(ex, "Error while expiring stale clients."))
            .DisposeWith(_compDisp);
    }

    public IObservable<IChangeSet<ClientSession, string>> Connect()
    {
        return _cache.Connect();
    }

    public IEnumerable<ClientSession> Sessions
    {
        get
        {
            lock (_sync) return _cache.Items.ToList();
        }
    }

    public ClientSession? GetSession(string id, EndPoint? source = null)
    {
        lock (_sync)
        {
            var lookup = _cache.Lookup(WsjtxMessageUtil.GetClientKey(id, source));
            return lookup.HasValue ? lookup.Value : null;
        }
    }

    public void Touch(WsjtxMessage message, EndPoint? source = null)
    {
        if (_disposed) return;

        var key = WsjtxMessageUtil.GetClientKey(message.Id, source);

        lock (_sync)
        {
            var lookup = _cache.Lookup(key);
            ClientSession client;

            if (lookup.HasValue)
            {
                client = lookup.Value;
            }
            else
            {
                // Derived from the key, not the raw Id: an empty Id is keyed by source address,
                // so labelling it from the Id would print "Unknown" for a session keyed "10.0.0.5".
                client = new ClientSession(key, WsjtxMessageUtil.GetClientDisplayName(key));
                _cache.AddOrUpdate(client);
                ClassLogger.Info($"New client registered: {client.DisplayName} ({client.Id})");
            }

            client.LastSeenUtc = DateTime.UtcNow;
            client.IsAlive = true;

            // Don't let a touch that carried no endpoint erase provenance we already have.
            var sourceAddress = WsjtxMessageUtil.GetSourceAddress(source);
            if (sourceAddress is not null) client.SourceAddress = sourceAddress;

            switch (message)
            {
                case Status status:
                    _applyStatus(client, status);
                    break;
                case Decode:
                    client.DecodedCount += 1;
                    break;
                case QsoLogged:
                    client.QsosLoggedCount += 1;
                    break;
                case Close:
                    client.IsAlive = false;
                    break;
            }
        }
    }

    private static void _applyStatus(ClientSession client, Status status)
    {
        client.DialFrequencyInHz = status.DialFrequencyInHz;
        client.Band = FreqHelper.GetMeterFromFreq(status.DialFrequencyInHz);
        client.Mode = status.Mode ?? string.Empty;
        client.DECall = status.DECall ?? string.Empty;
        client.DEGrid = status.DEGrid ?? string.Empty;
        client.DXCall = status.DXCall ?? string.Empty;
        client.DXGrid = status.DXGrid ?? string.Empty;
        client.TxEnabled = status.TXEnabled;
        client.Transmitting = status.Transmitting;
        client.Decoding = status.Decoding;
        client.ConfigurationName = status.ConfigurationName ?? string.Empty;
        client.TXMessage = status.TXMessage ?? string.Empty;
    }

    private void _expireStaleSessions()
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;

        lock (_sync)
        {
            foreach (var client in _cache.Items)
            {
                if (!client.IsAlive) continue;
                if (now - client.LastSeenUtc <= TimeSpan.FromSeconds(DefaultConfigs.UDPClientExpiryInSeconds))
                    continue;

                client.IsAlive = false;
                client.Transmitting = false;
                ClassLogger.Info($"Client expired: {client.DisplayName} ({client.Id})");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _compDisp.Dispose();
        _cache.Dispose();
    }
}
