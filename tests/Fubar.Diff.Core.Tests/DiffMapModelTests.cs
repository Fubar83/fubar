using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The location map's decisions, made where they can be tested without a window.
///
/// The map answers two questions a scrollbar cannot - "where are the changes" and "how much is left" -
/// and one that WinMerge's location pane also cannot: "how MUCH changed here", which is what separates a
/// stray edit from a rewritten block once a long file is squashed into a few hundred pixels.
/// </summary>
public class DiffMapModelTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int n) => new(n, "a", n, "b", ChangeKind.Modified);

    private static DiffLine Inserted(int n) => new(null, null, n, "new", ChangeKind.Inserted);

    private static DiffLine Deleted(int n) => new(n, "gone", null, null, ChangeKind.Deleted);

    private static DiffLine Ignored(int n) => new(n, "a", n, "b", ChangeKind.Unchanged) { IsIgnored = true };

    private static List<DiffLine> Document(int length, params (int Row, DiffLine Line)[] changes)
    {
        var lines = Enumerable.Range(1, length).Select(Unchanged).ToList();
        foreach (var (row, line) in changes)
        {
            lines[row] = line;
        }

        return lines;
    }

    private static DiffMapView Build(
        IReadOnlyList<DiffLine> lines,
        int pixelHeight = 100,
        int? scale = null,
        int viewportStart = 0,
        int viewportLength = 0)
    {
        var hunks = Hunks(lines);
        return DiffMapModel.Build(lines, hunks, pixelHeight, scale ?? lines.Count, viewportStart, viewportLength);
    }

    /// <summary>Contiguous runs of changed rows - the same grouping the navigator uses.</summary>
    private static List<DiffHunk> Hunks(IReadOnlyList<DiffLine> lines)
    {
        var hunks = new List<DiffHunk>();
        var start = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].IsChange)
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                hunks.Add(new DiffHunk(start, i - 1));
                start = -1;
            }
        }

        if (start >= 0) hunks.Add(new DiffHunk(start, lines.Count - 1));

        return hunks;
    }

    // ---- Density: the reason this is not one rectangle per hunk --------------------------------

    [Fact]
    public void A_dense_region_reads_as_denser_than_a_single_stray_edit()
    {
        // The whole point. 6000 rows over 100 pixels is 60 rows per pixel: the old per-hunk drawing gave
        // both of these the same minimum-height tick, so forty changes looked exactly like one.
        var lines = Document(6000);
        lines[100] = Modified(101);                                  // one stray edit
        for (var i = 3000; i < 3060; i++) lines[i] = Modified(i + 1); // a rewritten block

        var view = Build(lines);

        var stray = view.Bands.First(b => b.Y == 100 * 100 / 6000);
        var block = view.Bands.First(b => b.Y == 3000 * 100 / 6000);

        Assert.True(block.Density > stray.Density,
            $"a full pixel ({block.Density}) should read denser than a single row ({stray.Density})");
        Assert.Equal(1.0, block.Density);
    }

    [Fact]
    public void A_single_changed_line_in_a_huge_file_is_still_visible()
    {
        // The floor. A map that loses one-line changes is worse than no map, because it is read as
        // "nothing here".
        var lines = Document(100_000, (50_000, Modified(50_001)));

        var view = Build(lines);

        var band = Assert.Single(view.Bands, b => b.Side == MapSide.Left);
        Assert.True(band.Density >= 0.15);
    }

    [Fact]
    public void Density_never_exceeds_one()
    {
        var lines = Enumerable.Range(1, 500).Select(Modified).ToList();

        Assert.All(Build(lines, pixelHeight: 10).Bands, b => Assert.InRange(b.Density, 0, 1));
    }

    // ---- Sides ---------------------------------------------------------------------------------

    [Fact]
    public void A_deletion_marks_only_the_left_and_an_insertion_only_the_right()
    {
        // What the two halves buy: which side a change is on, without relying on colour alone.
        var deleted = Build(Document(10, (5, Deleted(6))));
        var inserted = Build(Document(10, (5, Inserted(6))));

        Assert.Equal([MapSide.Left], deleted.Bands.Select(b => b.Side).Distinct());
        Assert.Equal([MapSide.Right], inserted.Bands.Select(b => b.Side).Distinct());
    }

    [Fact]
    public void A_modification_marks_both()
    {
        var view = Build(Document(10, (5, Modified(6))));

        Assert.Equal([MapSide.Left, MapSide.Right], view.Bands.Select(b => b.Side).Order());
    }

    [Fact]
    public void A_deletion_and_an_insertion_in_one_pixel_keep_their_own_sides()
    {
        // They do NOT merge into "modified". Accumulating per side is what makes this right: nothing on
        // the left was modified, it was deleted - and the right half says an insertion arrived. Two
        // marks facing each other is a more accurate picture of a replaced block than one word for both.
        var lines = Document(1000);
        lines[500] = Deleted(501);
        lines[501] = Inserted(502);

        var view = Build(lines, pixelHeight: 10);   // 100 rows per pixel - both land in one pixel

        Assert.Equal(ChangeKind.Deleted, view.Bands.Single(b => b.Side == MapSide.Left).Kind);
        Assert.Equal(ChangeKind.Inserted, view.Bands.Single(b => b.Side == MapSide.Right).Kind);
    }

    [Fact]
    public void Two_different_kinds_on_the_SAME_side_summarise_as_modified()
    {
        // Where the mixing rule actually applies: a modification and a deletion both mark the left half,
        // and once they share a pixel neither word alone describes it.
        var lines = Document(1000);
        lines[500] = Deleted(501);
        lines[501] = Modified(502);

        var view = Build(lines, pixelHeight: 10);

        Assert.Equal(ChangeKind.Modified, view.Bands.Single(b => b.Side == MapSide.Left).Kind);
    }

    // ---- Ignored rows --------------------------------------------------------------------------

    [Fact]
    public void An_ignored_row_is_marked_even_though_it_forms_no_hunk()
    {
        // It used to show nothing at all, leaving the reader unable to tell "identical" from "a rule is
        // hiding this" - which is exactly what they want to check after adding a rule.
        var view = Build(Document(10, (5, Ignored(6))));

        Assert.NotEmpty(view.Bands);
        Assert.All(view.Bands, b =>
        {
            Assert.True(b.IsIgnored);
            Assert.Equal(ChangeKind.Unchanged, b.Kind);
        });
    }

    [Fact]
    public void A_change_and_an_ignored_row_in_the_same_pixel_are_reported_separately()
    {
        // They are two different things, so the model reports two marks even where they collide - what
        // used to happen was that the change absorbed the ignore, which lost the ignore entirely on a
        // long file. Which of the two the reader SEES at a shared pixel is settled by the control, which
        // paints ignored marks first and changes over them.
        var lines = Document(1000);
        lines[500] = Ignored(501);
        lines[501] = Modified(502);

        var view = Build(lines, pixelHeight: 10);

        var left = view.Bands.Where(b => b.Side == MapSide.Left).ToList();

        Assert.Contains(left, b => b.IsIgnored);
        Assert.Contains(left, b => !b.IsIgnored && b.Kind == ChangeKind.Modified);
        Assert.All(left, b => Assert.Equal(5, b.Y));
    }

    // ---- One mark per difference ---------------------------------------------------------------

    [Fact]
    public void Consecutive_rows_of_one_difference_are_a_single_mark()
    {
        // The whole point of grouping. Twelve marks in a row all belonging to change #1 tell the reader
        // there are twelve differences, which is a lie the map used to tell on any file short enough
        // that each row got a pixel of its own.
        var lines = Document(40);
        for (var row = 10; row < 22; row++)
        {
            lines[row] = Modified(row + 1);
        }

        var view = Build(lines, pixelHeight: 40);

        var left = Assert.Single(view.Bands, b => b.Side == MapSide.Left);

        Assert.Equal(10, left.Y);
        Assert.Equal(12, left.Height);
        Assert.Equal(0, left.HunkIndex);
        Assert.Equal(1, left.Density); // every row it covers changed
    }

    [Fact]
    public void Two_differences_stay_two_marks_however_close_they_are()
    {
        // Grouping is by DIFFERENCE, not by adjacency: one unchanged line between two changes makes them
        // two differences, and they must still count as two after the gap rounds away to nothing.
        var lines = Document(1000);
        lines[500] = Modified(501);
        lines[502] = Modified(503);

        var view = Build(lines, pixelHeight: 10);

        var left = view.Bands.Where(b => b.Side == MapSide.Left).ToList();

        Assert.Equal(2, left.Count);
        Assert.Equal([0, 1], left.Select(b => b.HunkIndex));
        Assert.All(left, b => Assert.Equal(5, b.Y)); // both squashed onto the same pixel
    }

    [Fact]
    public void A_difference_squashed_into_one_pixel_is_a_thin_mark_not_a_full_one()
    {
        // Grouping must not cost the density encoding: three changed rows out of the hundred behind a
        // pixel is still a sliver, which is how a stray edit stays distinguishable from a rewrite.
        var lines = Document(1000);
        for (var row = 500; row < 503; row++)
        {
            lines[row] = Modified(row + 1);
        }

        var view = Build(lines, pixelHeight: 10);

        var left = Assert.Single(view.Bands, b => b.Side == MapSide.Left);

        Assert.Equal(1, left.Height);
        Assert.True(left.Density < 0.2, $"expected a sliver, got {left.Density}");
    }

    [Fact]
    public void A_replacement_marks_each_side_over_its_own_rows()
    {
        // One difference, but the deleted rows and the inserted ones are at different heights. Grouping
        // per (side, difference) is what keeps each half over the rows it is actually about.
        var lines = Document(40);
        lines[10] = Deleted(11);
        lines[11] = Deleted(12);
        lines[12] = Inserted(11);

        var view = Build(lines, pixelHeight: 40);

        var left = Assert.Single(view.Bands, b => b.Side == MapSide.Left);
        var right = Assert.Single(view.Bands, b => b.Side == MapSide.Right);

        Assert.Equal((10, 2), (left.Y, left.Height));
        Assert.Equal((12, 1), (right.Y, right.Height));
        Assert.Equal(left.HunkIndex, right.HunkIndex);
    }

    [Fact]
    public void A_run_of_ignored_rows_is_one_mark_and_a_second_run_is_another()
    {
        // Ignored rows form no hunk, so there is no difference to group them by. Adjacency is the
        // closest thing available, and it still answers "how many places is a rule hiding something?".
        var lines = Document(40);
        lines[5] = Ignored(6);
        lines[6] = Ignored(7);
        lines[20] = Ignored(21);

        var view = Build(lines, pixelHeight: 40);

        var left = view.Bands.Where(b => b.Side == MapSide.Left).ToList();

        Assert.Equal(2, left.Count);
        Assert.All(left, b => Assert.True(b.IsIgnored));
        Assert.Equal([(5, 2), (20, 1)], left.Select(b => (b.Y, b.Height)));
        Assert.All(left, b => Assert.Equal(-1, b.HunkIndex));
    }

    [Fact]
    public void Every_mark_names_the_difference_it_belongs_to()
    {
        // The control recolours the current difference by matching this against its own CurrentHunk, so
        // an index that does not address the hunk list would highlight the wrong mark or none.
        var lines = Document(40);
        lines[5] = Modified(6);
        lines[20] = Modified(21);
        lines[21] = Modified(22);

        var view = Build(lines, pixelHeight: 40);
        var hunks = Hunks(lines);

        Assert.All(
            view.Bands.Where(b => !b.IsIgnored),
            b =>
            {
                Assert.InRange(b.HunkIndex, 0, hunks.Count - 1);

                // One pixel per row here, so the mark's top is the hunk's own first row.
                Assert.Equal(hunks[b.HunkIndex].StartIndex, b.Y);
            });
    }

    // ---- Moves ---------------------------------------------------------------------------------

    [Fact]
    public void A_pixel_is_only_moved_when_every_change_behind_it_moved()
    {
        // The move colour means "you can skip this". Being wrong about that is worse than not saying it,
        // so one real edit in the pixel demotes the whole band.
        var lines = Document(1000);
        lines[500] = Deleted(501) with { LeftMoveId = 1 };
        lines[501] = Deleted(502);            // an ordinary deletion in the same pixel

        var view = Build(lines, pixelHeight: 10);

        Assert.All(view.Bands.Where(b => b.Side == MapSide.Left), b => Assert.False(b.IsMoved));
    }

    [Fact]
    public void A_pixel_of_nothing_but_moved_rows_is_marked_moved()
    {
        var lines = Document(1000);
        lines[500] = Deleted(501) with { LeftMoveId = 1 };
        lines[501] = Deleted(502) with { LeftMoveId = 1 };

        var view = Build(lines, pixelHeight: 10);

        Assert.All(view.Bands.Where(b => b.Side == MapSide.Left), b => Assert.True(b.IsMoved));
    }

    [Fact]
    public void Both_ends_of_a_move_are_linked()
    {
        // The one place WinMerge's connecting line carries information here. Everywhere else the panes
        // are row-aligned, so left and right are already at the same height and a line would join a
        // point to itself.
        var lines = Document(1000);
        lines[100] = Deleted(101) with { LeftMoveId = 7 };
        lines[800] = Inserted(801) with { RightMoveId = 7 };

        var link = Assert.Single(Build(lines, pixelHeight: 100).MoveLinks);

        Assert.Equal(10, link.FromY);
        Assert.Equal(80, link.ToY);
    }

    [Fact]
    public void A_move_that_barely_travelled_gets_no_line()
    {
        // It would be a squiggle inside a mark the reader can already see whole.
        var lines = Document(1000);
        lines[100] = Deleted(101) with { LeftMoveId = 7 };
        lines[102] = Inserted(103) with { RightMoveId = 7 };

        Assert.Empty(Build(lines, pixelHeight: 100).MoveLinks);
    }

    [Fact]
    public void A_move_with_only_one_end_present_is_not_linked()
    {
        var lines = Document(1000, (100, Deleted(101) with { LeftMoveId = 7 }));

        Assert.Empty(Build(lines, pixelHeight: 100).MoveLinks);
    }

    [Fact]
    public void The_links_are_capped_so_they_stay_information_rather_than_hatching()
    {
        var lines = Document(4000);
        for (var i = 0; i < 60; i++)
        {
            lines[i * 10] = Deleted(i * 10 + 1) with { LeftMoveId = i };
            lines[2000 + i * 10] = Inserted(2000 + i * 10 + 1) with { RightMoveId = i };
        }

        Assert.InRange(Build(lines, pixelHeight: 400).MoveLinks.Count, 1, 24);
    }

    // ---- How much is left ----------------------------------------------------------------------

    [Fact]
    public void Changes_off_screen_are_counted_on_each_side_of_the_viewport()
    {
        // "How much is left to review" - the question people scroll a diff they have already read to
        // answer, and one neither a scrollbar nor a location pane usually answers.
        var lines = Document(1000, (100, Modified(101)), (500, Modified(501)), (900, Modified(901)));

        var view = Build(lines, viewportStart: 400, viewportLength: 200);

        Assert.Equal(1, view.ChangesAbove);
        Assert.Equal(1, view.ChangesBelow);
    }

    [Fact]
    public void A_hunk_partly_in_view_counts_as_neither()
    {
        var lines = Document(1000);
        for (var i = 390; i < 410; i++) lines[i] = Modified(i + 1);

        var view = Build(lines, viewportStart: 400, viewportLength: 200);

        Assert.Equal(0, view.ChangesAbove);
        Assert.Equal(0, view.ChangesBelow);
    }

    [Fact]
    public void With_no_viewport_nothing_is_counted_rather_than_everything()
    {
        var lines = Document(1000, (100, Modified(101)));

        var view = Build(lines, viewportStart: 0, viewportLength: 0);

        Assert.Equal(0, view.ChangesAbove);
        Assert.Equal(0, view.ChangesBelow);
    }

    // ---- Scale and degenerate input ------------------------------------------------------------

    [Fact]
    public void A_document_shorter_than_the_pane_keeps_its_marks_level_with_their_lines()
    {
        // Callers pass max(rows, viewportRows) as the scale for exactly this: stretching a ten-line file
        // over the whole strip would put every mark far below the line it refers to, and the map sits
        // between the panes where it is read against the adjacent text.
        var lines = Document(10, (5, Modified(6)));

        var stretched = Build(lines, pixelHeight: 100, scale: 10);
        var levelled = Build(lines, pixelHeight: 100, scale: 50);

        Assert.Equal(50, stretched.Bands[0].Y);
        Assert.Equal(10, levelled.Bands[0].Y);
    }

    [Fact]
    public void An_identical_comparison_draws_nothing()
    {
        Assert.Empty(Build(Document(100)).Bands);
    }

    [Fact]
    public void Degenerate_input_yields_an_empty_map_rather_than_throwing()
    {
        var lines = Document(10, (5, Modified(6)));

        Assert.Empty(DiffMapModel.Build(lines, Hunks(lines), 0, 10, 0, 0).Bands);
        Assert.Empty(DiffMapModel.Build(lines, Hunks(lines), 100, 0, 0, 0).Bands);
        Assert.Empty(DiffMapModel.Build([], [], 100, 100, 0, 0).Bands);
    }

    [Fact]
    public void With_hunks_but_no_rows_the_map_degrades_instead_of_going_blank()
    {
        // Rows carry everything interesting, but a blank strip reads as "no changes" - the one wrong
        // answer a diff tool must never give. Without them every hunk is drawn on both sides.
        var view = DiffMapModel.Build([], [new DiffHunk(10, 20)], 100, 100, 0, 0);

        Assert.NotEmpty(view.Bands);
        Assert.Contains(view.Bands, b => b.Side == MapSide.Left);
        Assert.Contains(view.Bands, b => b.Side == MapSide.Right);
        Assert.All(view.Bands, b => Assert.InRange(b.Y, 10, 20));
    }

    // ---- Click to row ---------------------------------------------------------------------------

    [Fact]
    public void A_click_addresses_the_row_under_it()
    {
        Assert.Equal(0, DiffMapModel.RowAt(0.0, 1000, 1000));
        Assert.Equal(500, DiffMapModel.RowAt(0.5, 1000, 1000));
        Assert.Equal(999, DiffMapModel.RowAt(1.0, 1000, 1000));
    }

    [Fact]
    public void A_click_below_a_short_document_lands_on_its_last_line()
    {
        // Clamped to the DOCUMENT, not the scale. When the whole file fits on screen the scale is the
        // viewport, so the lower part of the strip addresses rows that do not exist - and a click there
        // must land on the last line rather than past the end.
        Assert.Equal(19, DiffMapModel.RowAt(1.0, scale: 100, totalLines: 20));
        Assert.Equal(19, DiffMapModel.RowAt(0.8, scale: 100, totalLines: 20));
    }

    [Fact]
    public void A_click_outside_the_strip_is_clamped_rather_than_negative()
    {
        Assert.Equal(0, DiffMapModel.RowAt(-3, 1000, 1000));
        Assert.Equal(999, DiffMapModel.RowAt(7, 1000, 1000));
    }

    [Fact]
    public void An_empty_comparison_addresses_no_row()
    {
        Assert.Equal(-1, DiffMapModel.RowAt(0.5, 0, 100));
        Assert.Equal(-1, DiffMapModel.RowAt(0.5, 100, 0));
    }

    // ---- Clicking near a change ----------------------------------------------------------------

    [Fact]
    public void A_click_near_a_change_snaps_to_it()
    {
        // Without this the map is unclickable exactly where it matters: on a long file one pixel is a
        // hundred rows, so hitting a single-line change is luck - and missing by one pixel scrolls a
        // hundred lines away from the thing that was aimed at.
        var hunks = new List<DiffHunk> { new(5000, 5000) };

        // The hunk sits at pixel 50 of 100. Aim four pixels above it.
        var row = DiffMapModel.SnapToNearestChange(
            hunks, fraction: 0.46, scale: 10_000, totalLines: 10_000, pixelHeight: 100, tolerancePixels: 6);

        Assert.Equal(5000, row);
    }

    [Fact]
    public void A_click_far_from_every_change_goes_where_it_was_aimed()
    {
        // Dragging the strip has to keep scrubbing smoothly through unchanged stretches; snapping
        // everything would make the map a list of buttons instead of a map.
        var hunks = new List<DiffHunk> { new(5000, 5000) };

        var row = DiffMapModel.SnapToNearestChange(
            hunks, fraction: 0.10, scale: 10_000, totalLines: 10_000, pixelHeight: 100, tolerancePixels: 6);

        Assert.Equal(DiffMapModel.RowAt(0.10, 10_000, 10_000), row);
    }

    [Fact]
    public void A_click_inside_a_tall_hunk_prefers_it_over_a_short_one_nearby()
    {
        // Distance is zero within a hunk's own band, so the one under the pointer always wins.
        var hunks = new List<DiffHunk> { new(0, 5000), new(5100, 5100) };

        var row = DiffMapModel.SnapToNearestChange(
            hunks, fraction: 0.30, scale: 10_000, totalLines: 10_000, pixelHeight: 100, tolerancePixels: 6);

        Assert.Equal(0, row);
    }

    [Fact]
    public void Snapping_lands_on_the_hunks_START_not_wherever_the_pointer_was()
    {
        // So a click on a mark scrolls to the beginning of that change, which is what the reader meant.
        var hunks = new List<DiffHunk> { new(4000, 6000) };

        var row = DiffMapModel.SnapToNearestChange(
            hunks, fraction: 0.55, scale: 10_000, totalLines: 10_000, pixelHeight: 100, tolerancePixels: 6);

        Assert.Equal(4000, row);
    }

    [Fact]
    public void Snapping_with_no_hunks_is_just_the_position()
    {
        Assert.Equal(
            DiffMapModel.RowAt(0.5, 1000, 1000),
            DiffMapModel.SnapToNearestChange([], 0.5, 1000, 1000, 100, 6));
    }

    [Fact]
    public void Every_band_lands_inside_the_map()
    {
        var lines = Enumerable.Range(1, 997).Select(Modified).ToList();

        Assert.All(Build(lines, pixelHeight: 60).Bands, b => Assert.InRange(b.Y, 0, 59));
    }
}
