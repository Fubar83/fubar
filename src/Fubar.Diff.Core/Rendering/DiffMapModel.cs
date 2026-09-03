using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Rendering;

/// <summary>Which half of the map a mark belongs in.</summary>
public enum MapSide
{
    Left,
    Right,
}

/// <summary>
/// One mark on one side of the map: a whole difference, not a row of one.
/// </summary>
/// <param name="Y">Pixel offset of the mark's top from the top of the map.</param>
/// <param name="Kind">The change to colour it by. <see cref="ChangeKind.Unchanged"/> means this band
/// exists only to show ignored rows.</param>
/// <param name="Density">How many rows this difference has, against what one pixel of the map
/// represents, 0..1. On a file long enough that one pixel covers many rows this is what separates a
/// stray edit from a rewritten block - the thing a map is read for. On a file with room it saturates at
/// 1 for everything, and <see cref="Height"/> carries the size instead.</param>
/// <param name="IsMoved">Every changed row behind this band belongs to a moved block.</param>
/// <param name="IsIgnored">This band is only ignored rows.</param>
/// <param name="Height">Pixel rows the mark spans, at least 1. One difference is ONE mark this tall,
/// which is why the map can be counted by eye.</param>
/// <param name="HunkIndex">Which difference this mark is, indexing the hunk list it was built from, or
/// -1 for a run of ignored rows (which forms no hunk and cannot be navigated to).</param>
public sealed record MapBand(
    int Y,
    MapSide Side,
    ChangeKind Kind,
    double Density,
    bool IsMoved,
    bool IsIgnored,
    int Height,
    int HunkIndex);

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
/// <para><b>One mark per DIFFERENCE, sized by how much of it changed.</b> Two obvious designs are both
/// wrong, and this has been each of them. Give every hunk a rectangle with a minimum height and the map
/// fails in exactly the case it exists for: on a 60,000-line file drawn 600px tall one pixel is a
/// hundred rows, every hunk is clamped to the same minimum, and forty changes in a rewritten region
/// look identical to one stray edit beside it. Emit a band per PIXEL row instead and that is fixed, but
/// a new lie appears at the other end of the scale - on a file that fits on screen, one twelve-line
/// difference becomes twelve separate marks with gaps between them, and the map answers "how many
/// differences are there?" with a number far too big.</para>
///
/// <para>So: rows are grouped by the hunk they belong to, giving one mark per difference that can be
/// counted by eye, and the changed rows behind it are still counted and reported as
/// <see cref="MapBand.Density"/>, which is drawn as WIDTH. The two encodings divide the work by scale
/// and neither has to carry the other. Where the map has room, one pixel is one row, every mark is full
/// width, and HEIGHT says how big each difference is. Where a hundred rows share a pixel, height can no
/// longer tell them apart and width takes over. Measure density against the pixels a mark SPANS rather
/// than against one pixel's worth of rows and the two collide: at ten pixels per row every multi-row
/// difference lands on the 0.15 floor while a single-row one comes out full width, and the map draws big
/// differences thinner than small ones.</para>
///
/// <para>Grouping by hunk rather than by adjacency matters: two differences separated by one unchanged
/// line are two marks, and stay two marks even when the gap between them rounds away to nothing. Runs
/// of ignored rows form no hunk, so they are grouped by adjacency instead - the closest thing to "the
/// same difference" available for something the differ decided was not one.</para>
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
            ? BuildBands(lines, hunks, pixelHeight, scale)
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

    /// <summary>
    /// The row a click should actually go to: the start of the nearest hunk when one is close, and the
    /// row under the cursor when none is.
    ///
    /// <para>Without this the map is unclickable in exactly the case it matters. On a 60,000-line file
    /// drawn 600px tall one pixel is a hundred rows, so a single-line change occupies one pixel and
    /// landing on it is luck; worse, landing one pixel off silently scrolls a hundred lines away from
    /// the thing that was aimed at. Snapping means a mark that can be SEEN can be HIT.</para>
    ///
    /// <para>The tolerance is in pixels rather than rows on purpose - it is a statement about the
    /// pointer, not about the document, and a row-based one would be far too generous on a long file and
    /// uselessly tight on a short one. Falls back to the plain position when nothing is near, so
    /// dragging the map still scrubs smoothly through unchanged stretches.</para>
    /// </summary>
    public static int SnapToNearestChange(
        IReadOnlyList<DiffHunk> hunks,
        double fraction,
        int scale,
        int totalLines,
        int pixelHeight,
        double tolerancePixels)
    {
        var row = RowAt(fraction, scale, totalLines);
        if (row < 0 || hunks is null || hunks.Count == 0 || pixelHeight <= 0 || scale <= 0)
        {
            return row;
        }

        var y = fraction * pixelHeight;
        var best = -1;
        var bestDistance = double.MaxValue;

        foreach (var hunk in hunks)
        {
            var top = hunk.StartIndex * (double)pixelHeight / scale;
            var bottom = hunk.EndIndex * (double)pixelHeight / scale;

            // Zero when the pointer is within the hunk's own band, so a click inside a tall hunk always
            // wins over a short one a few pixels away.
            var distance = y < top ? top - y : y > bottom ? y - bottom : 0;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = hunk.StartIndex;
            }
        }

        return bestDistance <= tolerancePixels && best >= 0 ? best : row;
    }

    /// <summary>Hunks with no row data: one mark per hunk on both sides at full density, since without
    /// rows there is nothing to say which side it was on or how much of it there is.</summary>
    private static List<MapBand> BandsFromHunks(IReadOnlyList<DiffHunk> hunks, int pixelHeight, int scale)
    {
        var bands = new List<MapBand>();

        for (var index = 0; index < hunks.Count; index++)
        {
            var hunk = hunks[index];
            var from = Math.Clamp(hunk.StartIndex * pixelHeight / scale, 0, pixelHeight - 1);
            var to = Math.Clamp(hunk.EndIndex * pixelHeight / scale, 0, pixelHeight - 1);
            var height = to - from + 1;

            bands.Add(new MapBand(from, MapSide.Left, ChangeKind.Modified, 1, false, false, height, index));
            bands.Add(new MapBand(from, MapSide.Right, ChangeKind.Modified, 1, false, false, height, index));
        }

        return bands;
    }

    private static List<MapBand> BuildBands(
        IReadOnlyList<DiffLine> lines, IReadOnlyList<DiffHunk> hunks, int pixelHeight, int scale)
    {
        var hunkOfRow = HunkPerRow(lines.Count, hunks);

        // One group per (side, difference), found by key and kept in order of creation so the output is
        // deterministic without depending on how a dictionary happens to enumerate.
        var groups = new List<Group>();
        var byKey = new Dictionary<(MapSide Side, int Key), int>();

        // Ignored rows form no hunk, so consecutive ones are collected into a run of their own. Keys are
        // negative to keep them out of the hunk indices' space; the run number rather than the row means
        // a stretch of ignored rows is one mark, the same as a difference is.
        var ignoredRun = -1;
        var previousIgnoredRow = int.MinValue;

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
                // An ignored row is Unchanged + IsIgnored, so it forms no hunk and the map used to show
                // nothing at all for it. That left the reader unable to tell "these are identical" from
                // "a rule is hiding this", which is exactly what they want to check after adding one.
                if (row != previousIgnoredRow + 1)
                {
                    ignoredRun++;
                }

                previousIgnoredRow = row;

                var ignoredKey = -2 - ignoredRun;
                Collect(groups, byKey, MapSide.Left, ignoredKey, y, ChangeKind.Unchanged, false, true, -1);
                Collect(groups, byKey, MapSide.Right, ignoredKey, y, ChangeKind.Unchanged, false, true, -1);
                continue;
            }

            var hunkIndex = hunkOfRow[row];

            // A changed row belonging to no hunk should not happen - both come from the same comparison -
            // but if it ever does, every such row must not collapse into one mark spanning the file.
            // Falling back to a per-pixel key gives the old behaviour for those rows and nothing worse.
            var key = hunkIndex >= 0 ? hunkIndex : int.MinValue / 2 + y;

            // Which halves this row is about. A deletion exists only on the left, an insertion only on
            // the right, a modification on both.
            if (line.Kind is ChangeKind.Deleted or ChangeKind.Modified)
            {
                Collect(
                    groups, byKey, MapSide.Left, key, y,
                    line.Kind, line.IsMovedOn(DiffSide.Left), false, hunkIndex);
            }

            if (line.Kind is ChangeKind.Inserted or ChangeKind.Modified)
            {
                Collect(
                    groups, byKey, MapSide.Right, key, y,
                    line.Kind, line.IsMovedOn(DiffSide.Right), false, hunkIndex);
            }
        }

        // Rows behind one pixel. Never below 1, or a map taller than the document divides by zero.
        var rowsPerPixel = Math.Max(1.0, scale / (double)pixelHeight);

        var bands = new List<MapBand>(groups.Count);
        foreach (var group in groups)
        {
            bands.Add(group.ToBand(rowsPerPixel));
        }

        // Top to bottom, left before right. Nothing depends on it to draw, but a map is a thing people
        // compare screenshots of, and tests read the first band.
        bands.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Side.CompareTo(b.Side));

        return bands;
    }

    /// <summary>
    /// Which hunk each row belongs to, or -1. Built once per map rather than searched per row: the map
    /// is redrawn on every scroll, and hunks on a large diff are numerous enough for that to matter.
    /// </summary>
    private static int[] HunkPerRow(int rowCount, IReadOnlyList<DiffHunk> hunks)
    {
        var owner = new int[rowCount];
        Array.Fill(owner, -1);

        for (var index = 0; index < hunks.Count; index++)
        {
            var hunk = hunks[index];
            var from = Math.Max(0, hunk.StartIndex);
            var to = Math.Min(rowCount - 1, hunk.EndIndex);

            for (var row = from; row <= to; row++)
            {
                owner[row] = index;
            }
        }

        return owner;
    }

    private static void Collect(
        List<Group> groups,
        Dictionary<(MapSide Side, int Key), int> byKey,
        MapSide side,
        int key,
        int y,
        ChangeKind kind,
        bool moved,
        bool ignored,
        int hunkIndex)
    {
        if (!byKey.TryGetValue((side, key), out var index))
        {
            index = groups.Count;
            byKey[(side, key)] = index;
            groups.Add(new Group(side, y, hunkIndex));
        }

        groups[index].Add(y, kind, moved, ignored);
    }

    /// <summary>
    /// How full a mark is, 0..1 - but never 0, because a mark that has any change at all must be
    /// visible. The floor is what stops a single-line change disappearing on a long file.
    /// </summary>
    private static double Density(int changedRows, double rowsPerPixel) =>
        Math.Clamp(changedRows / Math.Max(1.0, rowsPerPixel), 0.15, 1.0);

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

    /// <summary>
    /// One difference on one side, while its rows are still being counted.
    ///
    /// A class rather than a struct because it is held in a list and mutated in place; a struct would
    /// need writing back on every row, which is the kind of thing that works until someone forgets.
    /// </summary>
    private sealed class Group(MapSide side, int firstY, int hunkIndex)
    {
        // Held explicitly rather than read off the parameter: the parameter also seeds _lastY, and a
        // primary-constructor parameter that is both captured and used to initialise a field is a
        // warning precisely because which one you are reading stops being obvious.
        private readonly int _firstY = firstY;
        private int _lastY = firstY;
        private int _changed;
        private int _moved;
        private int _ignored;
        private ChangeKind _kind = ChangeKind.Unchanged;

        public void Add(int y, ChangeKind kind, bool moved, bool ignored)
        {
            // Rows arrive in order, so this only ever grows downwards - but taking the max costs nothing
            // and means a caller feeding them in any order still gets a mark that covers them all.
            _lastY = Math.Max(_lastY, y);

            if (ignored)
            {
                _ignored++;
                return;
            }

            // First kind wins, EXCEPT that Modified is the honest summary of a difference holding both
            // an insertion and a deletion - which is what a rewritten block is.
            if (_changed == 0)
            {
                _kind = kind;
            }
            else if (_kind != kind)
            {
                _kind = ChangeKind.Modified;
            }

            _changed++;
            if (moved)
            {
                _moved++;
            }
        }

        public MapBand ToBand(double rowsPerPixel)
        {
            var height = _lastY - _firstY + 1;

            // Against ONE PIXEL's worth of rows, not against the rows this mark spans on screen. Those
            // are different numbers whenever the map is taller than the document - at ten pixels per row
            // a twelve-row difference spans about 111 pixels, and dividing twelve by that put every
            // multi-row difference on the 0.15 floor while a single-row one computed 1/1 and came out
            // full width. The map drew big differences THINNER than small ones, which is the opposite of
            // what width is for.
            //
            // This way the two encodings stay separate and neither has to carry the other: on a file
            // with room, one pixel is one row, every difference is full width, and HEIGHT says how big
            // it is. On a file long enough that a hundred rows share a pixel, height can no longer tell
            // them apart, and width takes over - a three-row edit is a sliver beside a sixty-row rewrite.
            return _changed == 0
                ? new MapBand(
                    _firstY, side, ChangeKind.Unchanged, Density(_ignored, rowsPerPixel), false, true, height, -1)
                : new MapBand(
                    _firstY,
                    side,
                    _kind,
                    Density(_changed, rowsPerPixel),
                    // Moved only when EVERY changed row here moved. A difference mixing a move with a
                    // real edit is an edit: the move colour means "you can skip this", and being wrong
                    // about that is worse than not saying it.
                    _moved == _changed,
                    false,
                    height,
                    hunkIndex);
        }
    }
}
