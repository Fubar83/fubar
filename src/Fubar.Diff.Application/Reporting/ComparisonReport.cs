using System;
using System.Collections.Generic;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Application.Reporting;

/// <summary>
/// A finished comparison, reduced to what a report needs to say about it.
///
/// Separate from <see cref="FileComparison"/> on purpose: that carries everything the WINDOW needs -
/// aligned documents, semantic change objects, array key choices, format details - and a report needs
/// a fraction of it, in a shape that survives being written to a file and read by something that is
/// not this program. Building this first is also what lets the four renderers below stay renderers,
/// with no comparison logic of their own to disagree about.
/// </summary>
/// <param name="LeftPath">The left-hand file, as the user named it.</param>
/// <param name="RightPath">The right-hand file.</param>
/// <param name="AreIdentical">
/// True when nothing differs. Note that two files can be identical here and still not be
/// interchangeable on disk - see <paramref name="FormatDifference"/>.
/// </param>
/// <param name="Added">Lines present only on the right.</param>
/// <param name="Removed">Lines present only on the left.</param>
/// <param name="Changed">Lines present on both sides but differing.</param>
/// <param name="Moved">
/// Lines that are part of a block that moved rather than being written or removed. Counted among the
/// added and removed as well, because that is what they are on disk - this only says how many of them
/// a reader can skip.
/// </param>
/// <param name="SemanticChanges">
/// Structural differences, when the pair was compared as JSON. Null when it was compared as text, so
/// a consumer can tell "no structural differences" from "structure was never looked at".
/// </param>
/// <param name="FormatDifference">
/// How the encoding, byte order mark or line endings differ, or null when they do not. The one
/// difference that never reaches the panes, so a report that omitted it could call two files
/// identical when they are not interchangeable.
/// </param>
/// <param name="Hunks">The differing regions, in document order.</param>
public sealed record ComparisonReport(
    string LeftPath,
    string RightPath,
    bool AreIdentical,
    int Added,
    int Removed,
    int Changed,
    int Moved,
    int? SemanticChanges,
    string? FormatDifference,
    IReadOnlyList<ReportHunk> Hunks)
{
    /// <summary>
    /// Reduces a comparison to a report.
    ///
    /// <paramref name="contextLines"/> is how much unchanged text to keep either side of each hunk, in
    /// the same sense <c>diff -U</c> means it: enough to see where a change sits without reprinting
    /// the file. Zero prints only the differing rows.
    /// </summary>
    public static ComparisonReport Build(FileComparison comparison, int contextLines = 3)
    {
        var result = comparison.Result;
        var rows = result.Lines;

        var hunks = new List<ReportHunk>(result.Hunks.Count);

        for (var i = 0; i < result.Hunks.Count; i++)
        {
            var hunk = result.Hunks[i];

            var from = Math.Max(hunk.StartIndex - contextLines, 0);
            var to = Math.Min(hunk.EndIndex + contextLines, rows.Count - 1);

            var lines = new List<ReportRow>(to - from + 1);
            for (var row = from; row <= to; row++)
            {
                var line = rows[row];

                // Filler rows are dropped: they exist to keep two editors row-aligned, and a report is
                // not two editors. Keeping them would print blank lines that are in neither file.
                if (line.Kind == ChangeKind.Filler)
                {
                    continue;
                }

                lines.Add(new ReportRow(
                    line.LeftNumber,
                    line.LeftText,
                    line.RightNumber,
                    line.RightText,
                    line.Kind,
                    line.IsMoved));
            }

            hunks.Add(new ReportHunk(i + 1, lines));
        }

        return new ComparisonReport(
            comparison.Left.DisplayName,
            comparison.Right.DisplayName,
            result.AreIdentical,
            result.Inserted,
            result.Deleted,
            result.Modified,
            result.Moved,
            // Ignored changes are excluded, exactly as the app's own status bar excludes them: a rule
            // that does not visibly quieten the report is a rule the user cannot tell is working.
            comparison.IsSemantic ? CountReported(comparison.SemanticChanges) : null,
            comparison.FormatDifference.Any
                ? TextFormatComparer.Describe(comparison.Left.Format, comparison.Right.Format)
                : null,
            hunks);
    }

    private static int CountReported(IReadOnlyList<Core.Json.JsonChange> changes)
    {
        var reported = 0;

        foreach (var change in changes)
        {
            if (!change.IsIgnored)
            {
                reported++;
            }
        }

        return reported;
    }

    /// <summary>
    /// One line, the way every diff tool says it: what changed and by how much.
    ///
    /// Written once here rather than in each renderer, so the console summary, the text report and the
    /// HTML header cannot drift apart.
    /// </summary>
    public string Summary()
    {
        if (AreIdentical)
        {
            return FormatDifference is { } format
                ? $"Same content, different file format - {format}"
                : "The files are identical.";
        }

        var moved = Moved > 0 ? $", {Moved} moved" : string.Empty;
        var semantic = SemanticChanges is { } count ? $" ({count} structural)" : string.Empty;

        return $"{Hunks.Count} change(s){semantic} - {Added} added, {Removed} removed, {Changed} changed{moved}";
    }
}

/// <summary>One differing region, with the context asked for around it.</summary>
/// <param name="Number">1-based, so a reader can refer to "change 3" as the app's own status bar does.</param>
/// <param name="Rows">The lines, in document order, fillers removed.</param>
public sealed record ReportHunk(int Number, IReadOnlyList<ReportRow> Rows);

/// <summary>
/// One line of a report. Both sides are carried rather than one merged column, because a modified
/// line is two texts and a reader wants both.
/// </summary>
public sealed record ReportRow(
    int? LeftNumber,
    string? LeftText,
    int? RightNumber,
    string? RightText,
    ChangeKind Kind,
    bool IsMoved);
