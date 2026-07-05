using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services;

namespace CloudlogHelper.Tests;

public class QsoQueueStoreTests
{
    [Fact]
    public void Add_AssignsUuid_WhenMissing()
    {
        using var store = new QsoQueueStore();
        var qso = new RecordedCallsignDetail();

        store.Add(qso);

        Assert.False(string.IsNullOrWhiteSpace(qso.Uuid));
        Assert.Same(qso, Assert.Single(store.Items));
    }

    [Fact]
    public void AddRange_TrimsOldestItems_WhenCapacityExceeded()
    {
        using var store = new QsoQueueStore();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var items = Enumerable.Range(0, DefaultConfigs.MaxRealtimeQsoItems + 1)
            .Select(index => new RecordedCallsignDetail
            {
                Uuid = $"qso-{index}",
                DateTimeOff = start.AddMinutes(index)
            })
            .ToList();

        store.AddRange(items);

        Assert.Equal(DefaultConfigs.MaxRealtimeQsoItems, store.Items.Count());
        Assert.DoesNotContain(store.Items, x => x.Uuid == "qso-0");
        Assert.Contains(store.Items, x => x.Uuid == $"qso-{DefaultConfigs.MaxRealtimeQsoItems}");
    }

    [Fact]
    public void AddRange_WithEmptyInput_DoesNothing()
    {
        using var store = new QsoQueueStore();

        store.AddRange(Array.Empty<RecordedCallsignDetail>());

        Assert.Empty(store.Items);
    }

    [Fact]
    public void Remove_WithMissingUuid_DoesNothing()
    {
        using var store = new QsoQueueStore();
        store.Add(new RecordedCallsignDetail { Uuid = "existing" });

        store.Remove(new RecordedCallsignDetail());

        var item = Assert.Single(store.Items);
        Assert.Equal("existing", item.Uuid);
    }

    [Fact]
    public void RemoveByUuids_RemovesMatchingItems_AndIgnoresMissingKeys()
    {
        using var store = new QsoQueueStore();
        store.AddRange(new[]
        {
            new RecordedCallsignDetail { Uuid = "keep" },
            new RecordedCallsignDetail { Uuid = "remove" }
        });

        store.RemoveByUuids(new[] { "remove", "missing" });

        var remaining = Assert.Single(store.Items);
        Assert.Equal("keep", remaining.Uuid);
    }

    [Fact]
    public void Connect_EmitsChangesForAddAndRemove()
    {
        using var store = new QsoQueueStore();
        var reasons = new List<DynamicData.ChangeReason>();
        using var subscription = store.Connect().Subscribe(changes =>
        {
            reasons.AddRange(changes.Select(change => change.Reason));
        });
        var qso = new RecordedCallsignDetail { Uuid = "tracked" };

        store.Add(qso);
        store.Remove(qso);

        Assert.Contains(DynamicData.ChangeReason.Add, reasons);
        Assert.Contains(DynamicData.ChangeReason.Remove, reasons);
    }
}
