using System;
using System.Collections.Generic;
using System.Linq;
using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Store
{
    // lib/components/app_list_tile.dart AppListBuilder.sort
    // sortColumn: 0=added, 1=nameAuthor, 2=authorName, 3=releaseDate
    // sortOrder: 0=ascending, 1=descending
    public static class AppSort
    {
        public static List<TrackedApp> Sort(IReadOnlyList<TrackedApp> apps, int sortColumn, int sortOrder)
        {
            var desc = sortOrder == 1;
            if (sortColumn == 0)
            {
                var list = apps.ToList();
                if (desc) list.Reverse();
                return list;
            }
            if (sortColumn == 3)
            {
                // Null release dates sort last (upstream: nulls get cmp 1).
                return apps
                    .Select((a, i) => (a, i))
                    .OrderBy(t => t.a.ReleaseDate == null ? 1 : 0)
                    .ThenBy(t => t.a.ReleaseDate, Comparer<DateTimeOffset?>.Create((x, y) =>
                    {
                        if (x == null && y == null) return 0;
                        if (x == null) return 1;
                        if (y == null) return -1;
                        return desc ? y.Value.CompareTo(x.Value) : x.Value.CompareTo(y.Value);
                    }))
                    .ThenBy(t => t.i)
                    .Select(t => t.a)
                    .ToList();
            }
            Func<TrackedApp, string> keyFn = sortColumn == 2
                ? a => (a.FinalAuthor + a.FinalName).ToLowerInvariant()
                : a => (a.FinalName + a.FinalAuthor).ToLowerInvariant();
            return apps
                .Select((a, i) => (a, i))
                .OrderBy(t => keyFn(t.a), Comparer<string>.Create((x, y) =>
                    desc ? string.CompareOrdinal(y, x) : string.CompareOrdinal(x, y)))
                .ThenBy(t => t.i)
                .Select(t => t.a)
                .ToList();
        }

        // lib/components/app_list_tile.dart AppListBuilder.reorder
        public static List<TrackedApp> Reorder(
            IReadOnlyList<TrackedApp> apps, bool pinUpdates, bool buryNonInstalled,
            Func<TrackedApp, bool> hasUpdate)
        {
            IEnumerable<TrackedApp> result = apps;
            if (pinUpdates)
                result = result.OrderBy(a => hasUpdate(a) ? 0 : 1);
            if (buryNonInstalled)
                result = result.OrderBy(a => a.InstalledVersion == null ? 1 : 0);
            // renamed first, then pinned, then normal, disabled last; stable
            // within each bucket (upstream preserves encounter order).
            return result
                .OrderBy(a => a.Disabled ? 3 : a.HasPendingRepoRename ? 0 : a.Pinned ? 1 : 2)
                .ToList();
        }
    }
}
