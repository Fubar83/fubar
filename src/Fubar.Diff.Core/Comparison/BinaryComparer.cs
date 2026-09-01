using System;
using Fubar.Diff.Core.Files;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// What a comparison of two binary files can honestly say.
///
/// Deliberately little. A byte-level diff of two compressed files is noise - change one pixel in a PNG
/// and every byte after it moves - so this answers the questions that DO have useful answers: are they
/// the same, how big is each, and where do they first stop agreeing. For an image the picture answers
/// the rest better than any number could, and for anything else the hex around the first difference is
/// what a person would go looking for anyway.
/// </summary>
/// <param name="AreIdentical">True when the two files are byte-for-byte equal.</param>
/// <param name="LeftLength">The left file's size in bytes.</param>
/// <param name="RightLength">The right file's size in bytes.</param>
/// <param name="FirstDifference">
/// The offset of the first byte that differs, or null when the files are identical. Where one file is
/// a strict prefix of the other this is the point the shorter one ENDS: no byte disagrees, but that is
/// still where they stop being the same file.
/// </param>
/// <param name="DifferingBytes">
/// How many byte positions differ within the overlapping prefix. A rough measure of "how different",
/// never a diff: for compressed content one changed pixel moves everything after it and this reads
/// near total.
/// </param>
public sealed record BinaryComparison(
    bool AreIdentical,
    int LeftLength,
    int RightLength,
    int? FirstDifference,
    int DifferingBytes)
{
    /// <summary>The left document, kept so the view can show hex or a picture.</summary>
    public required BinaryDocument Left { get; init; }

    /// <summary>The right document.</summary>
    public required BinaryDocument Right { get; init; }

    /// <summary>True when both sides are images this app can display.</summary>
    public bool BothAreImages => Left.IsImage && Right.IsImage;

    /// <summary>True when the two files differ in length as well as, or instead of, in content.</summary>
    public bool LengthsDiffer => LeftLength != RightLength;
}

/// <summary>
/// Compares two files byte for byte.
///
/// One pass over two spans, which is the point: there is no alignment to get wrong here and nothing to
/// tune. What makes binary comparison useful is refusing to pretend it is a diff.
/// </summary>
public static class BinaryComparer
{
    public static BinaryComparison Compare(BinaryDocument left, BinaryDocument right)
    {
        var a = left.Bytes.Span;
        var b = right.Bytes.Span;

        var overlap = Math.Min(a.Length, b.Length);

        int? first = null;
        var differing = 0;

        for (var i = 0; i < overlap; i++)
        {
            if (a[i] != b[i])
            {
                first ??= i;
                differing++;
            }
        }

        // Where one file simply continues past the other, the first difference is that point. No byte
        // disagrees, but "identical up to here, and then one of them stops" is exactly what the reader
        // needs to be told, and reporting no difference at all would be wrong.
        if (first is null && a.Length != b.Length)
        {
            first = overlap;
        }

        return new BinaryComparison(
            AreIdentical: a.Length == b.Length && differing == 0,
            LeftLength: a.Length,
            RightLength: b.Length,
            FirstDifference: first,
            DifferingBytes: differing)
        {
            Left = left,
            Right = right,
        };
    }
}
