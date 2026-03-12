using System;
using System.Collections.Generic;
using System.Linq;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using DynamicData;

namespace CloudlogHelper.Services;

public class QsoQueueStore : IQsoQueueStore, IDisposable
{
    private readonly SourceCache<RecordedCallsignDetail, string> _cache = new(x => x.Uuid);

    public IObservable<IChangeSet<RecordedCallsignDetail, string>> Connect()
    {
        return _cache.Connect();
    }

    public IEnumerable<RecordedCallsignDetail> Items => _cache.Items;

    public void Add(RecordedCallsignDetail qso)
    {
        if (qso == null) throw new ArgumentNullException(nameof(qso));
        EnsureUuid(qso);
        _cache.AddOrUpdate(qso);
        TrimIfNeeded();
    }

    public void AddRange(IEnumerable<RecordedCallsignDetail> qsos)
    {
        if (qsos == null) throw new ArgumentNullException(nameof(qsos));

        var list = qsos.ToList();
        if (list.Count == 0) return;

        foreach (var qso in list)
        {
            EnsureUuid(qso);
        }

        _cache.AddOrUpdate(list);
        TrimIfNeeded();
    }

    public void Remove(RecordedCallsignDetail qso)
    {
        if (qso == null) throw new ArgumentNullException(nameof(qso));
        if (string.IsNullOrWhiteSpace(qso.Uuid)) return;
        _cache.Remove(qso.Uuid);
    }

    public void RemoveRange(IEnumerable<RecordedCallsignDetail> qsos)
    {
        if (qsos == null) throw new ArgumentNullException(nameof(qsos));
        var keys = qsos
            .Where(x => !string.IsNullOrWhiteSpace(x.Uuid))
            .Select(x => x.Uuid)
            .ToList();
        if (keys.Count == 0) return;
        _cache.RemoveKeys(keys);
    }

    public void RemoveByUuids(IEnumerable<string> ids)
    {
        if (ids == null) throw new ArgumentNullException(nameof(ids));
        _cache.RemoveKeys(ids);
    }

    private void TrimIfNeeded()
    {
        var overflow = _cache.Count - DefaultConfigs.MaxRealtimeQsoItems;
        if (overflow <= 0) return;

        var keysToRemove = _cache.Items
            .OrderBy(x => x.DateTimeOff)
            .Take(overflow)
            .Select(x => x.Uuid)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (keysToRemove.Count == 0) return;
        _cache.RemoveKeys(keysToRemove);
    }

    private static void EnsureUuid(RecordedCallsignDetail qso)
    {
        if (!string.IsNullOrWhiteSpace(qso.Uuid)) return;
        qso.Uuid = Guid.NewGuid().ToString();
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
