using System.Collections.Generic;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Merge;

/// <summary>
/// Builds the merged file content from a diff plus the user's per-hunk decisions.
///
/// Pure, and deliberately so: this is the ONLY thing that decides what gets written to disk, and it
/// reads the domain model rather than the editors. The editors' text contains filler lines and is a
/// view artifact - saving what they contain would write blank lines into the user's file.
/// </summary>
public static class MergedDocument
{
    /// <summary>
    /// Produces the merged lines.
    ///
    /// Unchanged rows contribute the base side's text. Within a hunk, the resolution decides which
    /// side is taken; an unresolved hunk keeps the base side, so a merge with no decisions round-trips
    /// the base file exactly.
    /// </summary>
    /// <param name="result">The diff the hunk indices refer to.</param>
    /// <param name="state">The user's decisions.</param>
    /// <param name="baseSide">
    /// The side being merged INTO - the file that gets saved. Right by convention (left = theirs/old,
    /// right = mine/new).
    /// </param>
    public static IReadOnlyList<string> Build(DiffResult result, MergeState state, DiffSide baseSide)
    {
        var lines = result.Lines;
        var merged = new List<string>(lines.Count);

        // Walk rows in order, consulting the hunk that contains each one. Hunks are ordered and
        // non-overlapping, so a single advancing cursor is enough - no need to search per row.
        var hunkCursor = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            while (hunkCursor < result.Hunks.Count && result.Hunks[hunkCursor].EndIndex < i)
            {
                hunkCursor++;
            }

            var inHunk = hunkCursor < result.Hunks.Count
                         && i >= result.Hunks[hunkCursor].StartIndex
                         && i <= result.Hunks[hunkCursor].EndIndex;

            var side = inHunk
                ? SideFor(state.For(hunkCursor), baseSide)
                : baseSide;

            // A filler on the chosen side means that side genuinely has no line here, so the merged
            // file gets nothing - NOT an empty line. This is what makes "take left" on a deletion
            // actually delete the line rather than blank it.
            if (TextOn(lines[i], side) is { } text)
            {
                merged.Add(text);
            }
        }

        return merged;
    }

    private static DiffSide SideFor(HunkResolution resolution, DiffSide baseSide) => resolution switch
    {
        HunkResolution.TakeLeft => DiffSide.Left,
        HunkResolution.TakeRight => DiffSide.Right,
        _ => baseSide,
    };

    /// <summary>The row's text on one side, or null when that side has no line here (a filler).</summary>
    private static string? TextOn(DiffLine row, DiffSide side) =>
        side == DiffSide.Left ? row.LeftText : row.RightText;

    /// <summary>
    /// Joins merged lines back into file content in the document's own format, so saving preserves
    /// the file's existing conventions rather than silently rewriting CRLF to LF or dropping the
    /// trailing newline.
    /// </summary>
    public static string ToText(IReadOnlyList<string> lines, TextFormat format)
    {
        if (lines.Count == 0)
        {
            // An empty file stays empty. Appending a terminator here would turn a zero-byte file into
            // a one-byte one.
            return string.Empty;
        }

        var terminator = Terminator(format.LineEnding);
        var text = string.Join(terminator, lines);

        return format.EndsWithNewline ? text + terminator : text;
    }

    private static string Terminator(LineEnding lineEnding) => lineEnding switch
    {
        LineEnding.Crlf => "\r\n",
        LineEnding.Cr => "\r",
        _ => "\n",
    };
}
