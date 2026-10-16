using System.Collections.Generic;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

public class ClientTabLayoutTests
{
    [Fact]
    public void NoClients_ProducesNothingAndHidesTheStrip()
    {
        var result = ClientTabLayout.Compute(Array.Empty<string>(), Array.Empty<string>());

        Assert.Empty(result.Keys);
        Assert.False(result.ShouldShowStrip);
    }

    [Fact]
    public void SingleClient_KeepsTheStripHidden()
    {
        var result = ClientTabLayout.Compute(new[] { "WSJT-X - 20m" }, Array.Empty<string>());

        Assert.Equal(new[] { "WSJT-X - 20m" }, result.Keys);
        Assert.False(result.ShouldShowStrip);
    }

    [Fact]
    public void TwoClients_ShowsTheStrip()
    {
        var result = ClientTabLayout.Compute(
            new[] { "WSJT-X - 20m", "WSJT-X - 40m" }, Array.Empty<string>());

        Assert.Equal(2, result.Keys.Count);
        Assert.True(result.ShouldShowStrip);
    }

    [Fact]
    public void QsoOnlyClient_StillGetsATab()
    {
        // A client that has produced QSOs but has no live session (e.g. imported logs) must
        // still appear, because the tab strip doubles as a filter over past QSOs.
        var result = ClientTabLayout.Compute(new[] { "WSJT-X - 20m" }, new[] { "JTDX" });

        Assert.Equal(new[] { "JTDX", "WSJT-X - 20m" }, Assert.IsType<List<string>>(result.Keys));
        Assert.True(result.ShouldShowStrip);
    }

    [Fact]
    public void DuplicateIdsAcrossSources_AreCollapsed()
    {
        var result = ClientTabLayout.Compute(
            new[] { "WSJT-X - 20m", "WSJT-X - 20m" },
            new[] { "WSJT-X - 20m" });

        Assert.Equal(new[] { "WSJT-X - 20m" }, result.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankQsoClientIds_AreIgnored(string? blank)
    {
        var result = ClientTabLayout.Compute(Array.Empty<string>(), new[] { blank! });

        Assert.Empty(result.Keys);
        Assert.False(result.ShouldShowStrip);
    }

    [Fact]
    public void BlankSessionIds_AreNotFiltered()
    {
        // Sessions come from the registry, which already buckets empty ids as UnknownClient, so
        // the layout must not silently drop what it is handed.
        var result = ClientTabLayout.Compute(new[] { "" }, Array.Empty<string>());

        Assert.Equal(new[] { "" }, result.Keys);
    }

    [Fact]
    public void LocalTab_IsPinnedAfterEveryRealClient()
    {
        // "WSJT-Z" sorts before "wsjt-a" under Ordinal, but both sort after "LOCAL"... so the
        // pin is the only thing that can move LOCAL to the end.
        var result = ClientTabLayout.Compute(
            new[] { "WSJT-Z", "wsjt-a" }, new[] { WsjtxMessageUtil.LocalClient });

        Assert.Equal(
            new[] { "WSJT-Z", "wsjt-a", WsjtxMessageUtil.LocalClient },
            result.Keys);
    }

    [Fact]
    public void LocalTab_Alone_IsNotReordered()
    {
        var result = ClientTabLayout.Compute(Array.Empty<string>(), new[] { WsjtxMessageUtil.LocalClient });

        Assert.Equal(new[] { WsjtxMessageUtil.LocalClient }, result.Keys);
        Assert.False(result.ShouldShowStrip);
    }

    [Fact]
    public void LocalTab_AlreadyLast_StaysPut()
    {
        var result = ClientTabLayout.Compute(
            new[] { "wsjt-a", "zzz-client" }, new[] { WsjtxMessageUtil.LocalClient });

        // LOCAL already sorts last here, so the pin must be a no-op.
        Assert.Equal(
            new[] { "wsjt-a", "zzz-client", WsjtxMessageUtil.LocalClient },
            result.Keys);
    }

    [Fact]
    public void Keys_UseOrdinalOrdering_NotCultureSensitiveOrdering()
    {
        // Ordinal puts every uppercase letter before every lowercase one; a culture-aware
        // comparer would interleave them. This pins the set's comparer.
        var result = ClientTabLayout.Compute(
            new[] { "wsjt-a", "WSJT-Z" }, Array.Empty<string>());

        Assert.Equal(new[] { "WSJT-Z", "wsjt-a" }, result.Keys);
    }

    [Fact]
    public void Compute_DoesNotMutateItsInputs()
    {
        var sessions = new List<string> { "WSJT-Z", "wsjt-a" };
        var qsos = new List<string> { WsjtxMessageUtil.LocalClient };

        ClientTabLayout.Compute(sessions, qsos);

        Assert.Equal(new[] { "WSJT-Z", "wsjt-a" }, sessions);
        Assert.Equal(new[] { WsjtxMessageUtil.LocalClient }, qsos);
    }
}
