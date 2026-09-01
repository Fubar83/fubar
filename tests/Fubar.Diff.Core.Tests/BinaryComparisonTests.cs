using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Comparing files that are not text: the byte comparison itself, the hex formatting, and the
/// translation of the two into the ordinary aligned rows every view already understands.
/// </summary>
public class BinaryComparisonTests
{
    private static BinaryDocument Doc(params byte[] bytes) =>
        new("f.bin", bytes, ImageFormatDetector.Detect(bytes));

    private static BinaryComparison Compare(byte[] left, byte[] right) =>
        BinaryComparer.Compare(Doc(left), Doc(right));

    [Fact]
    public void Identical_files_are_reported_as_identical()
    {
        var result = Compare([1, 2, 3], [1, 2, 3]);

        Assert.True(result.AreIdentical);
        Assert.Null(result.FirstDifference);
        Assert.Equal(0, result.DifferingBytes);
    }

    [Fact]
    public void Two_empty_files_are_identical()
    {
        var result = Compare([], []);

        Assert.True(result.AreIdentical);
        Assert.Null(result.FirstDifference);
    }

    [Fact]
    public void The_first_differing_byte_is_named()
    {
        var result = Compare([1, 2, 3, 4], [1, 2, 9, 4]);

        Assert.False(result.AreIdentical);
        Assert.Equal(2, result.FirstDifference);
        Assert.Equal(1, result.DifferingBytes);
    }

    [Fact]
    public void A_file_that_is_a_prefix_of_the_other_first_differs_where_it_ends()
    {
        // No byte disagrees, so a naive comparison reports no difference at all. The point the shorter
        // file stops is exactly what the reader needs to be shown.
        var result = Compare([1, 2, 3], [1, 2, 3, 4, 5]);

        Assert.False(result.AreIdentical);
        Assert.Equal(3, result.FirstDifference);
        Assert.Equal(0, result.DifferingBytes);
        Assert.True(result.LengthsDiffer);
    }

    [Fact]
    public void An_empty_file_against_a_full_one_is_a_difference()
    {
        var result = Compare([], [1]);

        Assert.False(result.AreIdentical);
        Assert.Equal(0, result.FirstDifference);
    }

    [Fact]
    public void The_lengths_are_reported_as_they_are()
    {
        var result = Compare([1, 2], [1, 2, 3]);

        Assert.Equal(2, result.LeftLength);
        Assert.Equal(3, result.RightLength);
    }

    // ---- Image recognition ------------------------------------------------------------------------

    [Theory]
    [InlineData(ImageFormat.Png, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 })]
    [InlineData(ImageFormat.Jpeg, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 })]
    [InlineData(ImageFormat.Gif, new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1 })]
    [InlineData(ImageFormat.Bmp, new byte[] { 0x42, 0x4D, 1, 2, 3 })]
    [InlineData(ImageFormat.Ico, new byte[] { 0x00, 0x00, 0x01, 0x00, 1 })]
    public void An_image_is_recognised_from_its_signature(ImageFormat expected, byte[] bytes) =>
        Assert.Equal(expected, ImageFormatDetector.Detect(bytes));

    [Fact]
    public void A_webp_is_recognised_through_its_riff_container()
    {
        byte[] bytes = [.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8, 9];

        Assert.Equal(ImageFormat.Webp, ImageFormatDetector.Detect(bytes));
    }

    [Fact]
    public void A_riff_that_is_not_a_webp_is_not_an_image()
    {
        // A WAV file is also RIFF. Claiming it as an image would put a decoder failure on screen where
        // a hex dump belongs.
        byte[] bytes = [.. "RIFF"u8, 1, 2, 3, 4, .. "WAVE"u8];

        Assert.Equal(ImageFormat.None, ImageFormatDetector.Detect(bytes));
    }

    [Fact]
    public void Ordinary_binary_content_is_not_an_image()
    {
        Assert.Equal(ImageFormat.None, ImageFormatDetector.Detect([0x4D, 0x5A, 0x90, 0x00]));
        Assert.Equal(ImageFormat.None, ImageFormatDetector.Detect([]));
        Assert.Equal(ImageFormat.None, ImageFormatDetector.Detect([0x89]));
    }

    [Fact]
    public void Both_sides_being_images_is_what_the_picture_view_asks()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        Assert.True(BinaryComparer.Compare(Doc(png), Doc(png)).BothAreImages);
        Assert.False(BinaryComparer.Compare(Doc(png), Doc([0x4D, 0x5A])).BothAreImages);
    }

    // ---- Binary sniffing --------------------------------------------------------------------------

    [Fact]
    public void A_nul_byte_makes_content_binary()
    {
        Assert.True(BinaryContent.LooksBinary([0x48, 0x00, 0x69]));
        Assert.False(BinaryContent.LooksBinary("hello"u8));
        Assert.False(BinaryContent.LooksBinary([]));
    }

    [Fact]
    public void Utf16_is_text_despite_being_full_of_nul_bytes()
    {
        // Every ASCII character in UTF-16 carries a zero byte. Without the BOM check first, every
        // UTF-16 file in the world would be refused as binary.
        Assert.False(BinaryContent.LooksBinary([0xFF, 0xFE, 0x48, 0x00, 0x69, 0x00]));
        Assert.False(BinaryContent.LooksBinary([0xFE, 0xFF, 0x00, 0x48, 0x00, 0x69]));
    }

    // ---- Hex formatting ---------------------------------------------------------------------------

    [Fact]
    public void A_hex_line_carries_the_offset_the_bytes_and_the_characters()
    {
        var line = HexDump.Line([.. "Hi"u8, 0x00], 0)!;

        Assert.StartsWith("00000000  ", line, StringComparison.Ordinal);
        Assert.Contains("48 69 00", line, StringComparison.Ordinal);

        // The unprintable byte becomes a dot rather than reaching a text renderer as a control code.
        Assert.EndsWith("Hi.", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_final_row_keeps_its_character_column_where_the_rows_above_put_it()
    {
        var full = HexDump.Line(new byte[32], 0)!;
        var partial = HexDump.Line(new byte[20], 16)!;

        // Same length means the characters start at the same column, which is what makes two dumps
        // readable side by side.
        Assert.Equal(full.Length, partial.Length + (16 - 4));
        Assert.StartsWith("00000010  ", partial, StringComparison.Ordinal);
    }

    [Fact]
    public void An_offset_inside_a_line_is_snapped_back_to_that_lines_start()
    {
        // Both sides must start their dump at the same offset or the columns stop lining up.
        Assert.Equal(HexDump.Line(new byte[64], 0x10), HexDump.Line(new byte[64], 0x17));
    }

    [Fact]
    public void Reading_past_the_end_gives_nothing()
    {
        Assert.Null(HexDump.Line([1, 2, 3], 16));
        Assert.Empty(HexDump.Build([], 0, 4));
    }

    // ---- Hex as an ordinary diff ------------------------------------------------------------------

    [Fact]
    public void Equal_bytes_produce_unchanged_rows()
    {
        var rows = HexDiff.Build(Compare([1, 2, 3], [1, 2, 3]));

        Assert.True(rows.AreIdentical);
        Assert.All(rows.Lines, l => Assert.Equal(ChangeKind.Unchanged, l.Kind));
    }

    [Fact]
    public void A_differing_row_is_modified_and_the_rest_is_not()
    {
        var left = new byte[48];
        var right = new byte[48];
        right[20] = 0xFF;

        var rows = HexDiff.Build(Compare(left, right));

        Assert.Equal(3, rows.Lines.Count);
        Assert.Equal(ChangeKind.Unchanged, rows.Lines[0].Kind);
        Assert.Equal(ChangeKind.Modified, rows.Lines[1].Kind);
        Assert.Equal(ChangeKind.Unchanged, rows.Lines[2].Kind);
    }

    [Fact]
    public void Bytes_only_one_side_has_are_an_insertion()
    {
        var rows = HexDiff.Build(Compare(new byte[16], new byte[48]));

        Assert.Equal(ChangeKind.Unchanged, rows.Lines[0].Kind);
        Assert.Equal(ChangeKind.Inserted, rows.Lines[1].Kind);
        Assert.Equal(ChangeKind.Inserted, rows.Lines[2].Kind);

        // Filler on the side that has nothing there, exactly as a text comparison does it - which is
        // what lets the two editors stay aligned without knowing this is hex.
        Assert.Null(rows.Lines[1].LeftText);
        Assert.NotNull(rows.Lines[1].RightText);
    }

    [Fact]
    public void Bytes_the_right_side_lost_are_a_deletion()
    {
        var rows = HexDiff.Build(Compare(new byte[32], new byte[16]));

        Assert.Equal(ChangeKind.Deleted, rows.Lines[1].Kind);
        Assert.Null(rows.Lines[1].RightText);
    }

    [Fact]
    public void Empty_files_produce_no_rows()
    {
        Assert.Empty(HexDiff.Build(Compare([], [])).Lines);
    }

    [Fact]
    public void The_rows_line_up_by_offset_on_both_sides()
    {
        // Positional alignment is the only honest one for bytes, and it is what makes the two dumps
        // readable next to each other. Every row that exists on both sides must describe the same
        // offset.
        var left = new byte[64];
        var right = new byte[64];
        right[0] = 1;
        right[40] = 1;

        var rows = HexDiff.Build(Compare(left, right));

        Assert.All(rows.Lines, row => Assert.Equal(row.LeftNumber, row.RightNumber));
    }
}
