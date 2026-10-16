using System.Net;
using CloudlogHelper.Resources;
using CloudlogHelper.Services;
using CloudlogHelper.Utils;
using DynamicData;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Tests;

public class ClientRegistryServiceTests
{
    private static Status _status(string id, ulong freqHz = 14_074_000, string mode = "FT8")
    {
        return new Status
        {
            Id = id,
            DialFrequencyInHz = freqHz,
            Mode = mode,
            DECall = "BA1ABC",
            DEGrid = "OM89",
            DXCall = "JA1ABC",
            DXGrid = "PM95"
        };
    }

    [Fact]
    public void Touch_Status_CreatesSessionWithBandModeAndFrequency()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(_status("WSJT-X - 20m"));

        var session = Assert.Single(registry.Sessions);
        Assert.Equal("WSJT-X - 20m", session.Id);
        Assert.Equal("20m", session.Band);
        Assert.Equal("FT8", session.Mode);
        Assert.Equal(14_074_000UL, session.DialFrequencyInHz);
        Assert.True(session.IsAlive);
    }

    [Fact]
    public void Touch_SameId_ReusesSession()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(_status("WSJT-X"));
        registry.Touch(new Decode { Id = "WSJT-X" });

        var session = Assert.Single(registry.Sessions);
        Assert.Equal("WSJT-X", session.Id);
    }

    [Fact]
    public void Touch_DistinctFullIds_KeepSeparateSessions()
    {
        using var registry = new ClientRegistryService();

        // These two collide if the id is truncated to 6 chars ("WSJT-X").
        registry.Touch(_status("WSJT-X - 20m", 14_074_000));
        registry.Touch(_status("WSJT-X - 40m", 7_074_000));

        Assert.Equal(2, registry.Sessions.Count());
        Assert.Equal("20m", registry.GetSession("WSJT-X - 20m")!.Band);
        Assert.Equal("40m", registry.GetSession("WSJT-X - 40m")!.Band);
        Assert.Equal("20m", registry.GetSession("WSJT-X - 20m")!.DisplayName);
    }

    [Fact]
    public void Touch_Decode_IncrementsDecodedCount()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(new Decode { Id = "JTDX" });
        registry.Touch(new Decode { Id = "JTDX" });

        Assert.Equal(2, registry.GetSession("JTDX")!.DecodedCount);
    }

    [Fact]
    public void Touch_QsoLogged_IncrementsQsosLoggedCount()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(new QsoLogged { Id = "JTDX" });

        Assert.Equal(1, registry.GetSession("JTDX")!.QsosLoggedCount);
    }

    [Fact]
    public void Touch_EmptyIdWithoutEndpoint_GoesToUnknownBucket()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(new Decode { Id = string.Empty });

        var session = Assert.Single(registry.Sessions);
        Assert.Equal(WsjtxMessageUtil.UnknownClient, session.Id);
        Assert.Equal(WsjtxMessageUtil.UnknownClient, session.DisplayName);
    }

    [Fact]
    public void Touch_Close_MarksSessionNotAlive()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(_status("WSJT-X"));
        registry.Touch(new Close { Id = "WSJT-X" });

        Assert.False(registry.GetSession("WSJT-X")!.IsAlive);
    }

    [Fact]
    public void Connect_EmitsAddForNewClient()
    {
        using var registry = new ClientRegistryService();
        var reasons = new List<ChangeReason>();
        using var subscription = registry.Connect().Subscribe(changes =>
        {
            reasons.AddRange(changes.Select(change => change.Reason));
        });

        registry.Touch(_status("WSJT-X"));

        Assert.Contains(ChangeReason.Add, reasons);
    }

    [Fact]
    public void Touch_EmptyIdWithEndpoint_KeysAndLabelsSessionBySourceAddress()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(new Decode { Id = string.Empty }, Source("10.0.0.5"));

        var session = Assert.Single(registry.Sessions);
        Assert.Equal("10.0.0.5", session.Id);
        Assert.Equal("10.0.0.5", session.DisplayName);
        Assert.Equal("10.0.0.5", session.SourceAddress);
    }

    [Fact]
    public void Touch_BlankIdsFromDifferentMachines_KeepSeparateSessions()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(new Decode { Id = string.Empty }, Source("10.0.0.5"));
        registry.Touch(new Decode { Id = string.Empty }, Source("10.0.0.6"));

        Assert.Equal(2, registry.Sessions.Count());
        Assert.NotNull(registry.GetSession(string.Empty, Source("10.0.0.5")));
        Assert.NotNull(registry.GetSession(string.Empty, Source("10.0.0.6")));
    }

    [Fact]
    public void Touch_SameMachineDifferentPort_ReusesOneSession()
    {
        using var registry = new ClientRegistryService();

        // A WSJT-X instance picks a new ephemeral source port on every start.
        registry.Touch(new Decode { Id = string.Empty }, Source("10.0.0.5", 5000));
        registry.Touch(new Decode { Id = string.Empty }, Source("10.0.0.5", 6000));

        var session = Assert.Single(registry.Sessions);
        Assert.Equal("10.0.0.5", session.Id);
    }

    [Fact]
    public void Touch_NonBlankId_KeepsTheIdButRecordsSourceAddress()
    {
        using var registry = new ClientRegistryService();

        registry.Touch(_status("WSJT-X - 20m"), Source("10.0.0.5"));

        var session = registry.GetSession("WSJT-X - 20m")!;
        Assert.Equal("WSJT-X - 20m", session.Id);
        Assert.Equal("20m", session.Band);
        Assert.Equal("10.0.0.5", session.SourceAddress);
    }

    [Fact]
    public void Touch_WithoutEndpoint_KeepsProvenanceAlreadyRecorded()
    {
        using var registry = new ClientRegistryService();
        registry.Touch(_status("WSJT-X - 20m"), Source("10.0.0.5"));

        registry.Touch(_status("WSJT-X - 20m"));

        Assert.Equal("10.0.0.5", registry.GetSession("WSJT-X - 20m")!.SourceAddress);
    }

    [Fact]
    public void GetSession_BlankIdWithoutEndpoint_StillFindsTheUnknownSession()
    {
        using var registry = new ClientRegistryService();
        registry.Touch(new Decode { Id = string.Empty });

        Assert.NotNull(registry.GetSession(string.Empty));

        // Passing an endpoint changes the derived key, so this is a different lookup - the
        // Unknown bucket is only reachable without one.
        Assert.Null(registry.GetSession(string.Empty, Source("10.0.0.9")));
    }

    [Fact]
    public void GetSession_BlankIdWithEndpoint_FindsTheFallbackSession()
    {
        using var registry = new ClientRegistryService();
        registry.Touch(new Decode { Id = string.Empty }, Source("10.0.0.5"));

        Assert.Equal("10.0.0.5", registry.GetSession(string.Empty, Source("10.0.0.5"))!.Id);
    }

    private static IPEndPoint Source(string address, int port = 5000) =>
        new(IPAddress.Parse(address), port);

    [Fact]
    public async Task StaleSessions_AreExpired_WhileFreshOnesStayAlive()
    {
        using var registry = new ClientRegistryService();
        registry.Touch(_status("WSJT-X - 20m"));
        registry.Touch(_status("WSJT-X - 40m"));

        // Fake the last-seen time instead of sleeping past the window: expiry runs on the
        // registry's own one-second tick, so the wait below only covers that tick.
        registry.GetSession("WSJT-X - 20m")!.LastSeenUtc =
            DateTime.UtcNow.AddSeconds(-DefaultConfigs.UDPClientExpiryInSeconds - 30);

        await WaitUntilAsync(() => !registry.GetSession("WSJT-X - 20m")!.IsAlive);

        Assert.False(registry.GetSession("WSJT-X - 20m")!.IsAlive);
        Assert.False(registry.GetSession("WSJT-X - 20m")!.Transmitting);
        Assert.True(registry.GetSession("WSJT-X - 40m")!.IsAlive);
    }

    [Fact]
    public async Task ExpiredSession_IsRevivedByALaterTouch()
    {
        using var registry = new ClientRegistryService();
        registry.Touch(_status("WSJT-X"));
        registry.GetSession("WSJT-X")!.LastSeenUtc =
            DateTime.UtcNow.AddSeconds(-DefaultConfigs.UDPClientExpiryInSeconds - 30);

        await WaitUntilAsync(() => !registry.GetSession("WSJT-X")!.IsAlive);
        registry.Touch(_status("WSJT-X"));

        Assert.True(registry.GetSession("WSJT-X")!.IsAlive);
    }

    [Fact]
    public void Dispose_StopsExpiry()
    {
        var registry = new ClientRegistryService();
        registry.Touch(_status("WSJT-X"));
        registry.Dispose();

        registry.GetSession("WSJT-X")!.LastSeenUtc = DateTime.UtcNow.AddMinutes(-10);

        Assert.True(registry.GetSession("WSJT-X")!.IsAlive);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
    }
}
