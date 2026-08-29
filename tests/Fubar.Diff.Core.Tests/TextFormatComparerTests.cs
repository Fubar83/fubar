using Fubar.Diff.Core.Files;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Format differences are invisible in the lines - the reader strips the BOM and splits on every
/// terminator - so these pin the only place they can be detected at all.
/// </summary>
public class TextFormatComparerTests
{
    private static TextFormat Utf8 => new("utf-8", HasByteOrderMark: false, LineEnding.Lf);

    [Fact]
    public void Identical_formats_differ_in_nothing()
    {
        var difference = TextFormatComparer.Compare(Utf8, Utf8);

        Assert.False(difference.Any);
        Assert.Equal(string.Empty, TextFormatComparer.Describe(Utf8, Utf8));
    }

    [Fact]
    public void A_byte_order_mark_on_one_side_only_is_a_difference()
    {
        var withBom = Utf8 with { HasByteOrderMark = true };

        Assert.True(TextFormatComparer.Compare(Utf8, withBom).ByteOrderMarkDiffers);
        Assert.Contains("byte order mark (absent vs present)", TextFormatComparer.Describe(Utf8, withBom), StringComparison.Ordinal);
    }

    [Fact]
    public void Different_line_endings_are_a_difference_and_both_are_named()
    {
        var crlf = Utf8 with { LineEnding = LineEnding.Crlf };

        Assert.True(TextFormatComparer.Compare(crlf, Utf8).LineEndingDiffers);
        Assert.Contains("line endings (CRLF vs LF)", TextFormatComparer.Describe(crlf, Utf8), StringComparison.Ordinal);
    }

    [Fact]
    public void Different_encodings_are_a_difference()
    {
        var utf16 = Utf8 with { EncodingName = "utf-16" };

        Assert.True(TextFormatComparer.Compare(Utf8, utf16).EncodingDiffers);
        Assert.Contains("encoding (utf-8 vs utf-16)", TextFormatComparer.Describe(Utf8, utf16), StringComparison.Ordinal);
    }

    /// <summary>Encoding names are labels, not identities - "UTF-8" and "utf-8" are the same encoding.</summary>
    [Fact]
    public void Encoding_names_compare_case_insensitively()
    {
        Assert.False(TextFormatComparer.Compare(Utf8, Utf8 with { EncodingName = "UTF-8" }).EncodingDiffers);
    }

    [Fact]
    public void A_missing_trailing_newline_is_a_difference()
    {
        var noNewline = Utf8 with { EndsWithNewline = false };

        Assert.True(TextFormatComparer.Compare(Utf8, noNewline).TrailingNewlineDiffers);
        Assert.Contains("trailing newline (present vs absent)", TextFormatComparer.Describe(Utf8, noNewline), StringComparison.Ordinal);
    }

    [Fact]
    public void Several_differences_are_all_reported()
    {
        var other = new TextFormat("utf-16", HasByteOrderMark: true, LineEnding.Crlf, EndsWithNewline: false);

        var described = TextFormatComparer.Describe(Utf8, other);

        Assert.Contains("encoding", described, StringComparison.Ordinal);
        Assert.Contains("byte order mark", described, StringComparison.Ordinal);
        Assert.Contains("line endings", described, StringComparison.Ordinal);
        Assert.Contains("trailing newline", described, StringComparison.Ordinal);
    }
}
