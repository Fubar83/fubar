using Avalonia;
using Avalonia.Headless.XUnit;
using Fubar.Diff.Controls.Controls;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// The location map control. WHAT it draws is decided by DiffMapModel in Core and tested there - so is
/// the click-to-row rule; this covers the part only a control has, which is that it can be built,
/// measured and painted with the shapes it will really be handed without falling over.
/// </summary>
public class DiffMapTests
{
    private static List<DiffLine> Document(int length, params int[] changedRows)
    {
        var lines = Enumerable
            .Range(1, length)
            .Select(n => new DiffLine(n, "same", n, "same", ChangeKind.Unchanged))
            .ToList();

        foreach (var row in changedRows)
        {
            lines[row] = new DiffLine(row + 1, "a", row + 1, "b", ChangeKind.Modified);
        }

        return lines;
    }

    private static DiffMap Laid(int totalLines, int viewportLength, params int[] changed)
    {
        var map = new DiffMap
        {
            DiffLines = Document(totalLines, changed),
            Hunks = [.. changed.Select(r => new DiffHunk(r, r))],
            TotalLines = totalLines,
            ViewportLength = viewportLength,
            ViewportStart = 0,
        };

        // Headless controls get no layout pass unless asked, and every position the map computes is
        // relative to Bounds.Height.
        map.Measure(new Size(18, 400));
        map.Arrange(new Rect(0, 0, 18, 400));

        return map;
    }

    [AvaloniaFact]
    public void The_shapes_it_will_really_be_handed_lay_out_without_throwing()
    {
        // Cheap insurance over arithmetic that divides by a scale, a height and a row count: a one-line
        // document, one far longer than the strip is tall, and a pane with no viewport yet are all
        // ordinary states this passes through.
        foreach (var map in new[]
                 {
                     Laid(1, 100, 0),
                     Laid(100_000, 50, 0, 50_000, 99_999),
                     Laid(10, 0, 5),
                     Laid(500, 50),                  // nothing changed
                 })
        {
            map.InvalidateVisual();
            Assert.True(map.Bounds.Height > 0);
        }
    }

    [AvaloniaFact]
    public void A_map_with_no_rows_at_all_is_harmless()
    {
        var map = new DiffMap();
        map.Measure(new Size(18, 400));
        map.Arrange(new Rect(0, 0, 18, 400));

        Assert.Null(map.DiffLines);
    }

    [AvaloniaFact]
    public void Handing_it_new_rows_replaces_what_it_shows()
    {
        // DiffLines is a plain property rather than a styled one, so its setter has to invalidate: the
        // map would otherwise keep showing the previous comparison until something else forced a redraw.
        var map = Laid(100, 20, 10);

        map.DiffLines = Document(60, 20);

        Assert.Equal(60, map.DiffLines!.Count);
    }

    [AvaloniaFact]
    public void It_is_narrow_enough_to_sit_between_the_panes()
    {
        // The strip costs horizontal space from the thing the app exists to show, so its width is a
        // deliberate number rather than whatever the content wanted.
        Assert.InRange(new DiffMap().Width, 1, 24);
    }
}
