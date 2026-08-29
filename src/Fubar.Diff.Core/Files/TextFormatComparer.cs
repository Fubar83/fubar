using System.Collections.Generic;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// How two files' <see cref="TextFormat"/>s differ, when they do.
///
/// This exists because a format difference is invisible in the lines themselves: the reader strips the
/// BOM before decoding and splits on every terminator, so a UTF-8-with-BOM file and its BOM-less twin -
/// or a CRLF file and its LF twin - produce byte-identical <c>Lines</c>. Without this, the tool reports
/// "the files are identical" about two files that are not, which is exactly the question someone opens
/// a diff to settle after their version control said otherwise.
/// </summary>
public sealed record TextFormatDifference(
    bool EncodingDiffers,
    bool ByteOrderMarkDiffers,
    bool LineEndingDiffers,
    bool TrailingNewlineDiffers)
{
    /// <summary>Nothing differs - the common case, and the one that needs no reporting.</summary>
    public static TextFormatDifference None { get; } = new(false, false, false, false);

    public bool Any => EncodingDiffers || ByteOrderMarkDiffers || LineEndingDiffers || TrailingNewlineDiffers;
}

/// <summary>
/// Compares two <see cref="TextFormat"/>s and describes the result for a human.
///
/// Pure, and in Core, so what counts as a format difference is decided in one testable place rather
/// than by whichever view happens to render it.
/// </summary>
public static class TextFormatComparer
{
    public static TextFormatDifference Compare(TextFormat left, TextFormat right) => new(
        !string.Equals(left.EncodingName, right.EncodingName, System.StringComparison.OrdinalIgnoreCase),
        left.HasByteOrderMark != right.HasByteOrderMark,
        left.LineEnding != right.LineEnding,
        left.EndsWithNewline != right.EndsWithNewline);

    /// <summary>
    /// A short phrase naming each difference and which side has what, e.g.
    /// <c>line endings (CRLF vs LF), byte order mark (present vs absent)</c>. Empty when nothing
    /// differs.
    ///
    /// Both sides are always named rather than just the fact that they differ: "the line endings
    /// differ" leaves the user to go and find out which is which, which is the whole thing they came
    /// here to learn.
    /// </summary>
    public static string Describe(TextFormat left, TextFormat right)
    {
        var difference = Compare(left, right);
        if (!difference.Any)
        {
            return string.Empty;
        }

        var parts = new List<string>(4);

        if (difference.EncodingDiffers)
        {
            parts.Add($"encoding ({left.EncodingName} vs {right.EncodingName})");
        }

        if (difference.ByteOrderMarkDiffers)
        {
            parts.Add($"byte order mark ({Present(left.HasByteOrderMark)} vs {Present(right.HasByteOrderMark)})");
        }

        if (difference.LineEndingDiffers)
        {
            parts.Add($"line endings ({Name(left.LineEnding)} vs {Name(right.LineEnding)})");
        }

        if (difference.TrailingNewlineDiffers)
        {
            parts.Add($"trailing newline ({Present(left.EndsWithNewline)} vs {Present(right.EndsWithNewline)})");
        }

        return string.Join(", ", parts);
    }

    private static string Present(bool value) => value ? "present" : "absent";

    private static string Name(LineEnding ending) => ending switch
    {
        LineEnding.Crlf => "CRLF",
        LineEnding.Cr => "CR",
        _ => "LF",
    };
}
