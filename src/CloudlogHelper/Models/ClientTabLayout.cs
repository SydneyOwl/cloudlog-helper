using System;
using System.Collections.Generic;
using System.Linq;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Models;

/// <summary>
///     Pure ordering rules for the QSO log panel's client tab strip. Kept free of UI and service
///     types so the union and pinning behaviour can be tested on its own.
/// </summary>
public static class ClientTabLayout
{
    /// <summary>
    ///     Result of <see cref="Compute"/>: the tabs to show, in display order.
    /// </summary>
    public readonly struct Result
    {
        public Result(IReadOnlyList<string> keys, bool shouldShowStrip)
        {
            Keys = keys;
            ShouldShowStrip = shouldShowStrip;
        }

        /// <summary>
        ///     Client keys in display order, deduplicated and ordinally sorted.
        /// </summary>
        public IReadOnlyList<string> Keys { get; }

        /// <summary>
        ///     The tab strip is hidden for a single client, so one tab alone is pointless.
        /// </summary>
        public bool ShouldShowStrip { get; }
    }

    /// <summary>
    ///     Union the live sessions with every client id that has ever produced a QSO, order them,
    ///     and pin the synthetic LOCAL tab last.
    /// </summary>
    public static Result Compute(IEnumerable<string> sessionIds, IEnumerable<string> qsoClientIds)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var id in sessionIds) ids.Add(id);
        foreach (var id in qsoClientIds)
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);

        var keys = ids.ToList();

        // Keep the synthetic LOCAL tab (imported/local-log QSOs) pinned after every real client.
        var localIndex = keys.IndexOf(WsjtxMessageUtil.LocalClient);
        if (localIndex >= 0 && localIndex != keys.Count - 1)
        {
            keys.RemoveAt(localIndex);
            keys.Add(WsjtxMessageUtil.LocalClient);
        }

        return new Result(keys, keys.Count > 1);
    }
}
