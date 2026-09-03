using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;
using Fubar.Diff.Controls.Rendering;

namespace Fubar.Diff.Controls.Controls;

/// <summary>
/// A location map of the whole comparison: where the changes are, which side each is on, how much
/// changed at each point, where the viewport sits, and how many changes remain off screen in each
/// direction. Click or drag to jump; hover to be told what is there.
///
/// <para>Answers what a scrollbar cannot - "where are the changes, and how much is left?" - without
/// scrolling. What it draws is decided by <see cref="DiffMapModel"/> in Core, so the interesting rules
/// (density, side attribution, move linking) are testable without a window; this class only paints.</para>
///
/// <para>It differs from the location pane it is modelled on in three ways that matter. It aggregates
/// per PIXEL rather than per hunk, so a rewritten block does not look like a stray edit once a long file
/// is squashed into a few hundred pixels. It needs no connecting lines between its two halves, because
/// the panes are row-aligned and both halves are already at the same height - so a line is drawn only
/// for a MOVE, which is the one case where the two ends really are at different heights. And it says how
/// many changes are above and below the viewport, which is the question people scroll a diff they have
/// already read in order to answer.</para>
///
/// <para>Lives in the app rather than Fubar.Controls because it knows what a hunk is; the design
/// system's rule is that anything bound to a domain concept stays app-side.</para>
/// </summary>
public sealed class DiffMap : Control
{
    /// <summary>The hunks to plot.</summary>
    public static readonly StyledProperty<IReadOnlyList<DiffHunk>?> HunksProperty =
        AvaloniaProperty.Register<DiffMap, IReadOnlyList<DiffHunk>?>(nameof(Hunks));

    /// <summary>Total rows in the comparison - the denominator for every position on the map.</summary>
    public static readonly StyledProperty<int> TotalLinesProperty =
        AvaloniaProperty.Register<DiffMap, int>(nameof(TotalLines));

    /// <summary>First row currently visible in the editors.</summary>
    public static readonly StyledProperty<int> ViewportStartProperty =
        AvaloniaProperty.Register<DiffMap, int>(nameof(ViewportStart));

    /// <summary>How many rows are visible, for sizing the viewport box.</summary>
    public static readonly StyledProperty<int> ViewportLengthProperty =
        AvaloniaProperty.Register<DiffMap, int>(nameof(ViewportLength));

    /// <summary>Index of the hunk the user is currently on, or -1. Drawn emphasised.</summary>
    public static readonly StyledProperty<int> CurrentHunkProperty =
        AvaloniaProperty.Register<DiffMap, int>(nameof(CurrentHunk), -1);

    static DiffMap()
    {
        // Any of these changing alters what is drawn; none of them affect layout.
        AffectsRender<DiffMap>(
            HunksProperty,
            TotalLinesProperty,
            ViewportStartProperty,
            ViewportLengthProperty,
            CurrentHunkProperty);
    }

    public DiffMap()
    {
        Width = 32;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IReadOnlyList<DiffHunk>? Hunks
    {
        get => GetValue(HunksProperty);
        set => SetValue(HunksProperty, value);
    }

    public int TotalLines
    {
        get => GetValue(TotalLinesProperty);
        set => SetValue(TotalLinesProperty, value);
    }

    public int ViewportStart
    {
        get => GetValue(ViewportStartProperty);
        set => SetValue(ViewportStartProperty, value);
    }

    public int ViewportLength
    {
        get => GetValue(ViewportLengthProperty);
        set => SetValue(ViewportLengthProperty, value);
    }

    public int CurrentHunk
    {
        get => GetValue(CurrentHunkProperty);
        set => SetValue(CurrentHunkProperty, value);
    }

    /// <summary>
    /// Row data. Without it the map draws nothing: kind, side, density, moves and ignored rows all come
    /// from here, and hunks alone cannot supply any of them.
    /// </summary>
    public IReadOnlyList<DiffLine>? DiffLines
    {
        get => _diffLines;
        set
        {
            _diffLines = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<DiffLine>? _diffLines;

    /// <summary>Raised with a 0-based row index when the user clicks or drags the map.</summary>
    public event EventHandler<int>? JumpRequested;

    // ---- Painting ----------------------------------------------------------------------------------

    /// <summary>Gutter either side of the strip, so marks do not touch its borders.</summary>
    private const double Inset = 2;

    /// <summary>The hairline down each edge that makes the strip read as its own column.</summary>
    private const double BorderWidth = 1;

    /// <summary>Height of the off-screen indicator at the top and bottom.</summary>
    private const double ArrowHeight = 5;

    /// <summary>
    /// The SMALLEST a mark is drawn. A difference taller than this is drawn at its own height.
    ///
    /// A single-line change on a long file rounds to one pixel, which is a hair - hard to see and
    /// impossible to aim at. Flooring every mark here makes a lone change legible without touching the
    /// density encoding, which is carried by WIDTH.
    /// </summary>
    private const double BandThickness = 5;

    /// <summary>
    /// How close the pointer must be to a change for a click to snap to it. Generous on purpose: the
    /// alternative is a map whose marks can be seen and not hit.
    /// </summary>
    private const double SnapTolerance = 12;

    public override void Render(DrawingContext context)
    {
        var height = Bounds.Height;
        var width = Bounds.Width;

        if (DiffLines is not { Count: > 0 } lines || Bounds.Height <= 0 || Scale <= 0)
        {
            return;
        }

        var view = DiffMapModel.Build(
            lines,
            Hunks ?? [],
            (int)Math.Floor(height),
            Scale,
            ViewportStart,
            ViewportLength);

        DrawBorders(context, height, width);

        // Half the strip per side, inside the borders and their gutter.
        var columnWidth = (width - (BorderWidth + Inset) * 2) / 2;

        // The current difference is shown by RECOLOURING its own marks, not by drawing anything extra
        // over or behind them. A wash and a bar across the strip were both tried and both drowned the
        // map: the marks were already the right shape and weight, and the only thing missing was which
        // of them is the one you are on.
        var accent = DiffLineColors.CurrentHunkAccent(this);

        // Ignored marks first, real changes over them. A difference and a run of ignored rows are two
        // separate marks now, and on a long file they routinely round onto the same pixel - so whichever
        // is painted last decides what the reader sees there. It must be the change: the ignored colour
        // means "a rule is hiding something here", and letting it tint a real edit says the opposite of
        // what is true. Two passes rather than a sort, because painting order is this control's business
        // and the model has better reasons to stay in document order.
        DrawBands(context, view.Bands.Where(b => b.IsIgnored), columnWidth, accent);
        DrawBands(context, view.Bands.Where(b => !b.IsIgnored), columnWidth, accent);

        DrawMoveLinks(context, view, width);
        DrawViewport(context, height, width);
        DrawOffScreenCounts(context, view, height, width);
        DrawHover(context, width);
    }

    /// <summary>
    /// The number of rows the map's full height represents.
    ///
    /// Not simply <see cref="TotalLines"/>: the map sits BETWEEN the two editors, so its marks are read
    /// against the adjacent text, and stretching a short document over the full height would put every
    /// mark far below the line it refers to. Scaling by the viewport instead means a document that fits
    /// on screen has its marks level with their lines, while anything longer compresses to fit.
    /// </summary>
    private int Scale => Math.Max(TotalLines, ViewportLength);

    private void DrawBands(
        DrawingContext context, IEnumerable<MapBand> bands, double columnWidth, IBrush? accent)
    {
        var edge = BorderWidth + Inset;

        foreach (var band in bands)
        {
            // Which difference a mark IS, rather than where it happens to sit. This used to compare the
            // mark's pixel row against the current hunk's pixel bounds, which needed a fudge at each end
            // and still recoloured a neighbour whenever two differences rounded onto adjacent pixels.
            // Now that a mark is a whole difference it can simply say which one.
            var isCurrent = accent is not null && band.HunkIndex >= 0 && band.HunkIndex == CurrentHunk;

            if ((isCurrent ? accent : BrushFor(band)) is not { } brush)
            {
                continue;
            }

            var x = band.Side == MapSide.Left ? edge : edge + columnWidth;

            // Density is shown as WIDTH from the centre outwards rather than as opacity: a faint mark on
            // a dark strip is easy to miss entirely, while a short one is still unmistakably present.
            // The floor in the model guarantees a single-line change keeps a visible sliver.
            // Floored well above a hairline as well as scaled by density: the mark IS the click target,
            // so how big it is decides whether the map can be used at all.
            var drawn = Math.Max(5, columnWidth * band.Density);
            var left = band.Side == MapSide.Left ? x + (columnWidth - drawn) : x;

            // A difference's own height, floored so a one-pixel one is still a mark you can see and hit.
            var thickness = Math.Max(BandThickness, band.Height);

            context.FillRectangle(brush, new Rect(left, band.Y, drawn, thickness));
        }
    }

    private IBrush? BrushFor(MapBand band)
    {
        if (band.IsIgnored)
        {
            return DiffLineColors.IgnoredBackground(this);
        }

        return band.IsMoved
            ? DiffLineColors.MovedBackground(this)
            : DiffLineColors.LineBackground(this, band.Kind);
    }

    /// <summary>
    /// Joins the two ends of a moved block.
    ///
    /// This is the one place the location-pane idea of a connecting line carries information here.
    /// Everywhere else the panes are row-aligned, so a line between the halves would join a point to
    /// itself; a move is the one case whose ends genuinely sit at different heights.
    /// </summary>
    private void DrawMoveLinks(DrawingContext context, DiffMapView view, double width)
    {
        if (view.MoveLinks.Count == 0 || DiffLineColors.MovedBackground(this) is not ISolidColorBrush moved)
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(moved.Color, 0.45), 1);
        var centre = width / 2;

        foreach (var link in view.MoveLinks)
        {
            // A shallow curve bowing out from the centre, so several links stay separable instead of
            // stacking into one vertical smear.
            var bulge = Math.Min(centre - Inset, Math.Abs(link.ToY - link.FromY) / 8.0);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(centre, link.FromY + 0.5), isFilled: false);
                ctx.CubicBezierTo(
                    new Point(centre + bulge, link.FromY + 0.5),
                    new Point(centre + bulge, link.ToY + 0.5),
                    new Point(centre, link.ToY + 0.5));
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>
    /// A hairline down each edge.
    ///
    /// The strip sits between two editors, and without them it reads as empty margin belonging to one of
    /// them rather than as a column of its own - which also makes it unclear that it is something you
    /// can click.
    /// </summary>
    private void DrawBorders(DrawingContext context, double height, double width)
    {
        if (!this.TryFindResource("BorderSubtle", out var resource) || resource is not IBrush brush)
        {
            return;
        }

        context.FillRectangle(brush, new Rect(0, 0, BorderWidth, height));
        context.FillRectangle(brush, new Rect(width - BorderWidth, 0, BorderWidth, height));
    }

    private void DrawViewport(DrawingContext context, double height, double width)
    {
        if (ViewportLength <= 0)
        {
            return;
        }

        var top = ViewportStart / (double)Scale * height;
        var boxHeight = Math.Max(ViewportLength / (double)Scale * height, 6);

        if (this.TryFindResource("TextSecondary", out var resource) && resource is ISolidColorBrush brush)
        {
            var pen = new Pen(new SolidColorBrush(brush.Color, 0.55), 1);

            // Inset by half the pen width so the 1px outline lands on whole pixels instead of
            // straddling two and rendering as a soft 2px smear.
            context.DrawRectangle(null, pen, new Rect(0.5, top + 0.5, width - 1, boxHeight));
        }
    }

    /// <summary>
    /// Triangles at the top and bottom when changes lie off screen that way - the map's answer to "how
    /// much is left to review". The exact counts are in the tooltip; the triangle only has to say
    /// "there is more up there", which is what stops a reader scrolling to check.
    /// </summary>
    private void DrawOffScreenCounts(DrawingContext context, DiffMapView view, double height, double width)
    {
        if (this.TryFindResource("TextSecondary", out var resource) is false || resource is not ISolidColorBrush brush)
        {
            return;
        }

        var fill = new SolidColorBrush(brush.Color, 0.75);
        var centre = width / 2;

        if (view.ChangesAbove > 0)
        {
            context.DrawGeometry(fill, null, Triangle(centre, 1, ArrowHeight, up: true));
        }

        if (view.ChangesBelow > 0)
        {
            context.DrawGeometry(fill, null, Triangle(centre, height - 1, ArrowHeight, up: false));
        }

        _offScreen = (view.ChangesAbove, view.ChangesBelow);
    }

    private static StreamGeometry Triangle(double centreX, double baseY, double size, bool up)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        var tipY = up ? baseY : baseY - size;
        var flatY = up ? baseY + size : baseY;

        ctx.BeginFigure(new Point(centreX, tipY), isFilled: true);
        ctx.LineTo(new Point(centreX - size / 2, flatY));
        ctx.LineTo(new Point(centreX + size / 2, flatY));
        ctx.EndFigure(true);

        return geometry;
    }

    /// <summary>A hairline at the pointer, so it is obvious which row a click would land on.</summary>
    private void DrawHover(DrawingContext context, double width)
    {
        if (_hoverY is not { } y || !this.TryFindResource("TextPrimary", out var resource) || resource is not ISolidColorBrush brush)
        {
            return;
        }

        context.DrawLine(new Pen(new SolidColorBrush(brush.Color, 0.5), 1), new Point(0, y + 0.5), new Point(width, y + 0.5));
    }

    // ---- Pointer -----------------------------------------------------------------------------------

    private double? _hoverY;
    private (int Above, int Below) _offScreen;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        RequestJump(e.GetPosition(this).Y);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var y = e.GetPosition(this).Y;
        _hoverY = y;
        UpdateTooltip(y);
        InvalidateVisual();

        // Only jump while dragging - captured means the press started here.
        if (Equals(e.Pointer.Captured, this))
        {
            RequestJump(y);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverY = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    /// <summary>
    /// Says what is under the pointer, and how much is off screen. A map you can only click is a map you
    /// have to click to read; naming the hunk turns "somewhere around there" into "change 12 of 40".
    /// </summary>
    private void UpdateTooltip(double y)
    {
        var row = RowAt(y);
        if (row < 0)
        {
            ToolTip.SetTip(this, null);
            return;
        }

        var parts = new List<string> { $"Line {row + 1:N0} of {TotalLines:N0}" };

        if (Hunks is { Count: > 0 } hunks)
        {
            var index = IndexOfHunkAt(hunks, row);
            parts.Add(index >= 0
                ? $"change {index + 1:N0} of {hunks.Count:N0}"
                : $"{hunks.Count:N0} changes");
        }

        if (_offScreen.Above > 0 || _offScreen.Below > 0)
        {
            parts.Add(string.Create(
                CultureInfo.CurrentCulture,
                $"{_offScreen.Above:N0} above, {_offScreen.Below:N0} below the view"));
        }

        ToolTip.SetTip(this, string.Join(" · ", parts));
    }

    /// <summary>The hunk containing a row, or the nearest one within a pixel's worth of rows either side -
    /// on a long file the row under the cursor is one of hundreds, and demanding an exact hit would make
    /// the tooltip almost never name a change.</summary>
    private int IndexOfHunkAt(IReadOnlyList<DiffHunk> hunks, int row)
    {
        var tolerance = Bounds.Height > 0 ? (int)Math.Ceiling(Scale / Bounds.Height) : 0;

        for (var i = 0; i < hunks.Count; i++)
        {
            if (row >= hunks[i].StartIndex - tolerance && row <= hunks[i].EndIndex + tolerance)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The row a point on the strip addresses. The rules are in Core, where they are tested;
    /// this only supplies the geometry.</summary>
    private int RowAt(double y) =>
        Bounds.Height <= 0 ? -1 : DiffMapModel.RowAt(y / Bounds.Height, Scale, TotalLines);

    /// <summary>Where a CLICK goes - the nearest change when one is close, so a mark that can be seen
    /// can be hit.</summary>
    private int ClickRowAt(double y) =>
        Bounds.Height <= 0
            ? -1
            : DiffMapModel.SnapToNearestChange(
                Hunks ?? [], y / Bounds.Height, Scale, TotalLines, (int)Math.Floor(Bounds.Height), SnapTolerance);

    private void RequestJump(double y)
    {
        var row = ClickRowAt(y);
        if (row >= 0)
        {
            JumpRequested?.Invoke(this, row);
        }
    }
}
