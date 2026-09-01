using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Controls.Rendering;

namespace Fubar.Diff.Controls.Controls;

/// <summary>
/// A scaled-down map of the whole comparison: one tick per hunk, positioned proportionally through the
/// document, coloured by change kind, with a box showing what the viewport is currently on.
///
/// Answers the question a scrollbar cannot - "where are the changes, and how many are left?" - without
/// scrolling. Clicking or dragging jumps straight to a position.
///
/// Lives in the app rather than Fubar.Controls because it knows what a hunk is; the design system's
/// rule is that anything bound to a domain concept stays app-side.
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
        Width = 14;
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

    /// <summary>Raised with a 0-based row index when the user clicks or drags the map.</summary>
    public event EventHandler<int>? JumpRequested;

    public override void Render(DrawingContext context)
    {
        var hunks = Hunks;
        var total = Scale;

        if (hunks is null || hunks.Count == 0 || total <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var height = Bounds.Height;
        var width = Bounds.Width;

        for (var i = 0; i < hunks.Count; i++)
        {
            var hunk = hunks[i];

            var top = hunk.StartIndex / (double)total * height;
            var rawHeight = hunk.Length / (double)total * height;

            // Floor the height: on a long file a one-line hunk is a fraction of a pixel and would
            // vanish, which defeats the point of a map you scan for changes.
            var tickHeight = Math.Max(rawHeight, MinimumTickHeight);

            if (BrushFor(hunk) is { } brush)
            {
                var isCurrent = i == CurrentHunk;

                // The current hunk spans the full width; the rest are inset, so the one you are on
                // reads at a glance without needing a different colour.
                var inset = isCurrent ? 0 : 2;
                context.FillRectangle(brush, new Rect(inset, top, width - inset * 2, tickHeight));
            }
        }

        DrawViewport(context, total, height, width);
    }

    private const double MinimumTickHeight = 3;

    /// <summary>
    /// The number of rows the map's full height represents.
    ///
    /// Not simply <see cref="TotalLines"/>: the map sits BETWEEN the two editors, so its ticks are
    /// read against the adjacent text, and stretching a short document over the full height would put
    /// every tick far below the line it refers to. Scaling by the viewport instead means a document
    /// that fits on screen has its ticks level with their lines, while anything longer compresses to
    /// fit exactly as before.
    /// </summary>
    private int Scale => Math.Max(TotalLines, ViewportLength);

    /// <summary>
    /// The tick's colour: blue for a hunk that only moved, otherwise the ordinary tint for its kind.
    ///
    /// The map is where the reason for marking moves pays off most - it is read as "how much is left
    /// to review", and a reordered file that fills it end to end is answering that question wrongly
    /// until the moves are a different colour.
    /// </summary>
    private IBrush? BrushFor(DiffHunk hunk) =>
        DiffLines is { Count: > 0 } lines && hunk.StartIndex < lines.Count && lines[hunk.StartIndex].IsMoved
            ? DiffLineColors.MovedBackground(this)
            : DiffLineColors.LineBackground(this, KindOf(hunk));

    /// <summary>
    /// The kind used to colour a hunk. A hunk can mix kinds; the first row's kind is a good enough
    /// summary for a 14px-wide indicator, and picking the "worst" kind would need an ordering that
    /// does not really exist between inserted, deleted and modified.
    /// </summary>
    private ChangeKind KindOf(DiffHunk hunk) =>
        DiffLines is { Count: > 0 } lines && hunk.StartIndex < lines.Count
            ? lines[hunk.StartIndex].Kind
            : ChangeKind.Modified;

    /// <summary>
    /// Optional row data, used only to colour ticks by kind. Left null, every hunk draws as modified -
    /// the map still works, it is just monochrome.
    /// </summary>
    public IReadOnlyList<DiffLine>? DiffLines { get; set; }

    private void DrawViewport(DrawingContext context, int total, double height, double width)
    {
        if (ViewportLength <= 0)
        {
            return;
        }

        var top = ViewportStart / (double)total * height;
        var boxHeight = Math.Max(ViewportLength / (double)total * height, MinimumTickHeight * 2);

        if (this.TryFindResource("TextSecondary", out var resource) && resource is ISolidColorBrush brush)
        {
            var pen = new Pen(new SolidColorBrush(brush.Color, 0.55), 1);

            // Inset by half the pen width so the 1px outline lands on whole pixels instead of
            // straddling two and rendering as a soft 2px smear.
            context.DrawRectangle(null, pen, new Rect(0.5, top + 0.5, width - 1, boxHeight));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        RequestJump(e.GetPosition(this).Y);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        // Only while dragging - captured means the press started here.
        if (Equals(e.Pointer.Captured, this))
        {
            RequestJump(e.GetPosition(this).Y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void RequestJump(double y)
    {
        // Same scale the ticks were drawn at, so a click lands on the tick under the cursor.
        var total = Scale;
        if (total <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(y / Bounds.Height, 0, 1);

        // Clamp to the document, not to the scale: when the whole file fits on screen the scale is
        // larger than the row count, so the lower part of the map addresses rows that do not exist.
        JumpRequested?.Invoke(this, Math.Clamp((int)(ratio * total), 0, TotalLines - 1));
    }
}
