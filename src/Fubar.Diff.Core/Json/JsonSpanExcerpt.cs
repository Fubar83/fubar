using System;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Isolates just the lines a <see cref="SourceSpan"/> covers, for the Json view's close-up pane: a
/// change addresses a span in the FULL raw document, but showing that whole document there would
/// defeat the point of a close-up - the same reason <c>AlignedText.BuildCompact</c> exists for Text
/// mode's detail pane. Column numbers are left as given; only the line numbers shift to the excerpt's
/// own numbering, which is all <c>RawJsonPane</c>'s highlight renderer reads.
/// </summary>
public static class JsonSpanExcerpt
{
    /// <summary>
    /// Extracts the span's own lines from <paramref name="rawText"/> and returns them alongside the
    /// same span renumbered to start at line 1. An unknown span (no node on this side - an insertion
    /// or deletion) yields an empty excerpt.
    /// </summary>
    public static (string Text, SourceSpan Span) Build(string rawText, SourceSpan span)
    {
        if (!span.IsKnown)
        {
            return (string.Empty, SourceSpan.None);
        }

        var lines = rawText.Split('\n');
        var startIndex = Math.Clamp(span.StartLine - 1, 0, lines.Length - 1);
        var endIndex = Math.Clamp(span.EndLine - 1, startIndex, lines.Length - 1);

        var excerpt = string.Join('\n', lines[startIndex..(endIndex + 1)]);
        var excerptSpan = new SourceSpan(1, span.StartColumn, endIndex - startIndex + 1, span.EndColumn);

        return (excerpt, excerptSpan);
    }
}
