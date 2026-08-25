using System;
using System.Collections.Generic;

namespace Fubar.Diff.Core.Settings;

/// <summary>
/// The rules for the recent list: most recent first, no duplicates, capped.
///
/// In Core as a pure function because the de-duplication is the part that is easy to get wrong -
/// re-opening a pair should MOVE it to the top, not add a second copy - and that is worth a test
/// rather than an assumption buried in a view model.
/// </summary>
public static class RecentComparisons
{
    /// <summary>
    /// Returns the list with this pair at the front.
    ///
    /// Paths are compared case-insensitively on Windows and case-sensitively elsewhere, matching how
    /// the file system itself behaves - otherwise the same file opened via a differently-cased path
    /// would appear twice on Windows.
    /// </summary>
    public static IReadOnlyList<RecentComparison> Add(
        IReadOnlyList<RecentComparison> existing,
        string left,
        string right,
        int max = AppSettings.MaxRecent)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return existing;
        }

        var entry = new RecentComparison(left, right);
        var result = new List<RecentComparison>(existing.Count + 1) { entry };

        foreach (var candidate in existing)
        {
            if (result.Count >= max)
            {
                break;
            }

            if (!IsSamePair(candidate, entry))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>Drops entries whose files no longer exist, using a caller-supplied probe.</summary>
    public static IReadOnlyList<RecentComparison> Prune(
        IReadOnlyList<RecentComparison> existing,
        Func<string, bool> exists)
    {
        var result = new List<RecentComparison>(existing.Count);

        foreach (var entry in existing)
        {
            if (exists(entry.Left) && exists(entry.Right))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static bool IsSamePair(RecentComparison a, RecentComparison b) =>
        string.Equals(a.Left, b.Left, PathComparison)
        && string.Equals(a.Right, b.Right, PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
