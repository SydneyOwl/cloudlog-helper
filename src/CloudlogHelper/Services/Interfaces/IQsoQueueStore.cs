using System;
using System.Collections.Generic;
using CloudlogHelper.Models;
using DynamicData;

namespace CloudlogHelper.Services.Interfaces;

public interface IQsoQueueStore
{
    IObservable<IChangeSet<RecordedCallsignDetail, string>> Connect();
    IEnumerable<RecordedCallsignDetail> Items { get; }
    void Add(RecordedCallsignDetail qso);
    void AddRange(IEnumerable<RecordedCallsignDetail> qsos);
    void Remove(RecordedCallsignDetail qso);
    void RemoveRange(IEnumerable<RecordedCallsignDetail> qsos);
    void RemoveByUuids(IEnumerable<string> ids);
}
