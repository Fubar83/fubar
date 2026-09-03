using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Rendering;

/// <summary>Which half of the map a mark belongs in.</summary>
public enum MapSide
{
    Left,
    Right,
}

/// <summary>
/// One pixel row of one side of the map.
/// </summary>
/// <param name="Y">Pixel offset from the top of the map.</param>
/// <param name="Kind">The change to colour it by. <see cref="ChangeKind.Unchanged"/> means this band
/// exists only to show an ignored row.</param>
/// <param name="Density">How much of this pixel's worth of rows actually changed, 0..1. On a file long
/// enough that one pixel covers many rows, this is what separates a single stray edit from a rewritten
/// block - the thing a map is read for.</param>
/// <param name="IsMoved">Every changed row behind this band belongs to a moved block.</param>
/// <param name="IsIgnored">This band is only ignored rows.</param>
public sealed record MapBand(int Y, MapSide Side, ChangeKind Kind, double Density, bool IsMoved, bool IsIgnored);

/// <summary>Both ends of one moved block, in pixel rows, so the map can join them up.</summary>
public sealed record MapMoveLink(int FromY, int ToY);

/// <summary>Everything the map draws, in pixels, already aggregated.</summary>
/// <param name="ChangesAbove">Hunks entirely above the viewport.</param>
/// <param name="ChangesBelow">Hunks entirely below it.</param>
public sealed record DiffMapView(
    IReadOnlyList<MapBand> Bands,
    IReadOnlyList<MapMoveLink> MoveLinks,
    int ChangesAbove,
    int ChangesBelow)
{
    public static readonly DiffMapView Empty = new([], [], 0, 0);
}

/// <summary>
/// Turns a comparison into the marks a location map draws.
///
/// <para><b>Aggregated per PIXEL, not per hunk.</b> The obvious implementation gives every hunk a
/// rectangle and a minimum height so it cannot vanish, and that is what was here before. It fails in
/// exactly the case a map exists for: on a 60,000-line file drawn 600px tall, one pixel is a hundred
/// rows, every hunk is clamped to the same minimum, and forty changes in a rewritten region look
/// identical to one stray edit beside it. Counting the changed rows behind each pixel and reporting
/// that as <see cref="MapBand.Density"/> makes "how much changed here" legible again, which is the
/// question a map is read for and the one WinMerge's location pane cannot answer either.</para>
///
/// <para><b>Per side.</b> The map sits between two aligned panes, so a mark can say which side it is
/// about: a deletion paints only the left half, an insertion only the right, a modification both. That
/// costs nothing here precisely because the panes are row-aligned - row <c>i</c> is the same row in both
/// documents - which is also why this needs none of WinMerge's connecting lines between its two columns.
/// Its columns are at independent scales and the lines exist to tie them together; ours are the same
/// scale by construction.</para>
///
/// <para>The one place a connecting line DOES carry information is a move, whose two ends are at
/// different rows by definition - see <see cref="MapMoveLink"/>.</para>
///
/// <para>Pure, and in Core, so every one of these decisions is testable without a window.</para>
/// </summary>
public static class DiffMapModel
{
    /// <summary>A move whose ends are closer together than this is not worth drawing a line for - it
    /// would be a squiggle inside a mark the reader can already see whole.</summary>
    private const int MinimumMoveSpanPixels = 6;

    /// <summary>Past this many moves the links stop being information and become hatching.</summary>
    private const int MaximumMoveLinks = 24;

    /// <summary>
    /// Builds the map.
    /// </summary>
    /// <param name="scale">Rows the map's full height represents. Callers pass
    /// <c>max(totalRows, viewportRows)</c> so a document shorter than the pane keeps its marks level
    /// with the lines they refer to instead of being stretched over the whole strip.</param>
    public static DiffMapView Build(
        IReadOnlyList<DiffLine> lines,
        IReadOnlyList<DiffHunk> hunks,
        int pixelHeight,
        int scale,
        int viewportStart,
        int viewportLength)
    {
        if (lines is null || hunks is null || pixelHeight <= 0 || scale <= 0)
        {
            return DiffMapView.Empty;
        }

        // Rows carry everything interesting - kind, side, density, moves, ignores - but a caller that
        // has only hunks must still get a usable map rather than a blank strip. Degrading is the right
        // failure here: a map that silently shows nothing reads as "no changes", which is the one wrong
        // answer a diff tool must never give.
        var bands = lines.Count > 0
            ? BuildBands(lines, pixelHeight, scale)
            : BandsFromHunks(hunks, pixelHeight, scale);

        var links = lines.Count > 0 ? BuildMoveLinks(lines, pixelHeight, scale) : [];

        var (above, below) = CountOffScreen(hunks, viewportStart, viewportLength);

        return new DiffMapView(bands, links, above, below);
    }

    /// <summary>
    /// The row a click at <paramref name="fraction"/> of the way down the map addresses.
    ///
    /// <para>Clamped to the DOCUMENT, not to the scale. When the whole file fits on screen the scale is
    /// the viewport rather than the row count, so the lower part of the strip addresses rows that do not
    /// exist - and a click there must land on the last line rather than past the end.</para>
    /// </summary>
    public static int RowAt(double fraction, int scale, int totalLines)
    {
        if (scale <= 0 || totalLines <= 0)
        {
            return -1;
        }

        return Math.Clamp((int)(Math.Clamp(fraction, 0, 1) * scale), 0, totalLines - 1);
    }

    /// <summary>Hunks with no row data: every change drawn on both sides at full density, since without
    /// rows there is nothing to say which side it was on or how much of it there is.</summary>
    private static List<MapBand> BandsFromHunks(IReadOnlyList<DiffHunk> hunks, int pixelHeight, int scale)
    {
        var seen = new HashSet<int>();
        var bands = new List<MapBand>();

        foreach (var hunk in hunks)
        {
            var from = Math.Clamp(hunk.StartIndex * pixelHeight / scale, 0, pixelHeight - 1);
            var to = Math.Clamp(hunk.EndIndex * pixelHeight / scale, 0, pixelHeight - 1);

            for (var y = from; y <= to; y++)
            {
                if (!seen.Add(y))
                {
                    continue;
                }

                bands.Add(new MapBand(y, MapSide.Left, ChangeKind.Modified, 1, false, false));
                bands.Add(new MapBand(y, MapSide.Right, ChangeKind.Modified, 1, false, false));
            }
        }

        return bands;
    }

    private static List<MapBand> BuildBands(IReadOnlyList<DiffLine> lines, int pixelHeight, int scale)
    {
        // Two accumulators per pixel row, one per side.
        var left = new Accumulator[pixelHeight];
        var right = new Accumulator[pixelHeight];

        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            if (!line.IsChange && !line.IsIgnored)
            {
                continue;
            }

            var y = row * pixelHeight / scale;
            if (y < 0 || y >= pixelHeight)
            {
                continue;
            }

            if (line.IsIgnored)
            {
                // An ignored row is Unchanged + IsIgnored, so it forms no hunk and the old map showed
                // nothing at all for it. That left the reader unable to tell "these are identical" from
                // "a rule is hiding this", which is exactly what they want to check after adding one.
                left[y].AddIgnored();
                right[y].AddIgnored();
                continue;
            }

            // Which halves this row is about. A deletion exists only on the left, an insertion only on
            // the right, a modification on both.
            if (line.Kind is ChangeKind.Deleted or ChangeKind.Modified)
            {
                left[y].Add(line.Kind, line.IsMovedOn(DiffSide.Left));
            }

            if (line.Kind is ChangeKind.Inserted or ChangeKind.Modified)
            {
                right[y].Add(line.Kind, line.IsMovedOn(DiffSide.Right));
            }
        }

        // Rows behind one pixel. Never below 1, or a map taller than the document divides by zero.
        var rowsPerPixel = Math.Max(1.0, scale / (double)pixelHeight);

        var bands = new List<MapBand>();
        for (var y = 0; y < pixelHeight; y++)
        {
            Emit(bands, left[y], y, MapSide.Left, rowsPerPixel);
            Emit(bands, right[y], y, MapSide.Right, rowsPerPixel);
        }

        return bands;
    }

    private static void Emit(List<MapBand> into, Accumulator accumulator, int y, MapSide side, double rowsPerPixel)
    {
        if (accumulator.Total == 0 && accumulator.Ignored == 0)
        {
            return;
        }

        if (accumulator.Total == 0)
        {
            into.Add(new MapBand(y, side, ChangeKind.Unchanged, Density(accumulator.Ignored, rowsPerPixel), false, true));
            return;
        }

        into.Add(new MapBand(
            y,
            side,
            accumulator.Kind,
            Density(accumulator.Total, rowsPerPixel),
            // Moved only when EVERY changed row here moved. A pixel mixing a move with a real edit is
            // an edit: the move colour means "you can skip this", and being wrong about that is worse
            // than not saying it.
            accumulator.Moved == accumulator.Total,
            false));
    }

    /// <summary>
    /// How full this pixel is, 0..1 - but never 0, because a pixel that has any change at all must be
    /// visible. The floor is what stops a single-line change disappearing on a long file.
    /// </summary>
    private static double Density(int rows, double rowsPerPixel) =>
        Math.Clamp(rows / rowsPerPixel, 0.15, 1.0);

    private static List<MapMoveLink> BuildMoveLinks(IReadOnlyList<DiffLine> lines, int pixelHeight, int scale)
    {
        // Where each move id starts on each side. First row is enough: the link says "this block came
        // from there", and drawing every row of it would be a filled shape rather than a connection.
        var from = new Dictionary<int, int>();
        var to = new Dictionary<int, int>();

        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            if (!line.IsChange)
            {
                continue;
            }

            if (line.LeftMoveId is { } leftId)
            {
                from.TryAdd(leftId, row);
            }

            if (line.RightMoveId is { } rightId)
            {
                to.TryAdd(rightId, row);
            }
        }

        var links = new List<MapMoveLink>();
        foreach (var (id, fromRow) in from)
        {
            if (!to.TryGetValue(id, out var toRow))
            {
                continue; // only one half is on screen in this comparison
            }

            var fromY = fromRow * pixelHeight / scale;
            var toY = toRow * pixelHeight / scale;

            if (Math.Abs(fromY - toY) >= MinimumMoveSpanPixels)
            {
                links.Add(new MapMoveLink(fromY, toY));
            }
        }

        // Deterministic, and the longest travel first so the cap keeps the moves worth seeing.
        links.Sort((a, b) => Math.Abs(b.ToY - b.FromY).CompareTo(Math.Abs(a.ToY - a.FromY)));

        return links.Count > MaximumMoveLinks ? links[..MaximumMoveLinks] : links;
    }

    /// <summary>
    /// Hunks wholly outside the viewport, which is the map's answer to "how much is left?" - a question
    /// neither a scrollbar nor WinMerge's location pane answers, and the reason people scroll a diff
    /// they have already read.
    /// </summary>
    private static (int Above, int Below) CountOffScreen(
        IReadOnlyList<DiffHunk> hunks, int viewportStart, int viewportLength)
    {
        if (viewportLength <= 0)
        {
            return (0, 0);
        }

        var viewportEnd = viewportStart + viewportLength - 1;
        var above = 0;
        var below = 0;

        foreach (var hunk in hunks)
        {
            if (hunk.EndIndex < viewportStart)
            {
                above++;
            }
            else if (hunk.StartIndex > viewportEnd)
            {
                below++;
            }
        }

        return (above, below);
    }

    /// <summary>What one pixel row of one side collected.</summary>
    private struct Accumulator
    {
        public int Total;
        public int Moved;
        public int Ignored;
        public ChangeKind Kind;

        public void Add(ChangeKind kind, bool moved)
        {
            // First kind wins, EXCEPT that Modified is the honest summary of a pixel holding both an
            // insertion and a deletion - which is what a rewritten block looks like once it is squashed
            // into one pixel.
            if (Total == 0)
            {
                Kind = kind;
            }
            else if (Kind != kind)
            {
                Kind = ChangeKind.Modified;
            }

            Total++;
            if (moved)
            {
                Moved++;
            }
        }

        public void AddIgnored() => Ignored++;
    }
}
