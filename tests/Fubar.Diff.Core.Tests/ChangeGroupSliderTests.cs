using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The presentation pass over a finished alignment. Two things to prove: it MOVES a group when doing
/// so reads better, and it never changes what the diff says while doing it.
/// </summary>
public class ChangeGroupSliderTests
{
    /// <summary>
    /// Builds rows from a compact script, so a test reads as the diff it is about.
    /// Each entry is "=" for context, "-" for a deleted left line, "+" for an inserted right line.
    /// </summary>
    private static (IReadOnlyList<DiffLine> Rows, string[] Left, string[] Right) Build(params string[] script)
    {
        var rows = new List<DiffLine>();
        var left = new List<string>();
        var right = new List<string>();

        foreach (var entry in script)
        {
            var marker = entry[0];
            var text = entry[1..];

            switch (marker)
            {
                case '=':
                    left.Add(text);
                    right.Add(text);
                    rows.Add(new DiffLine(left.Count, text, right.Count, text, ChangeKind.Unchanged));
                    break;

                case '-':
                    left.Add(text);
                    rows.Add(new DiffLine(left.Count, text, null, null, ChangeKind.Deleted));
                    break;

                case '+':
                    right.Add(text);
                    rows.Add(new DiffLine(null, null, right.Count, text, ChangeKind.Inserted));
                    break;

                default:
                    throw new ArgumentException($"unknown marker '{marker}'", nameof(script));
            }
        }

        return (rows, [.. left], [.. right]);
    }

    private static IReadOnlyList<DiffLine> Compact(IReadOnlyList<DiffLine> rows, string[] left, string[] right) =>
        ChangeGroupSlider.Compact(rows, left, left, right, right);

    /// <summary>Renders the result as the same script shape, for a one-line assertion.</summary>
    private static string[] Script(IReadOnlyList<DiffLine> rows) =>
    [
        .. rows.Select(r => r.Kind switch
        {
            ChangeKind.Deleted => "-" + r.LeftText,
            ChangeKind.Inserted => "+" + r.RightText,
            _ => "=" + (r.LeftText ?? r.RightText),
        }),
    ];

    /// <summary>
    /// Each side's document, read back off the rows. Sliding must leave both EXACTLY as they were -
    /// that is what makes it a presentation pass rather than an edit.
    /// </summary>
    private static (string[] Left, string[] Right) Documents(IReadOnlyList<DiffLine> rows) =>
    (
        [.. rows.Where(r => r.LeftText is not null).Select(r => r.LeftText!)],
        [.. rows.Where(r => r.RightText is not null).Select(r => r.RightText!)]
    );

    [Fact]
    public void A_removed_method_slides_down_onto_its_own_body()
    {
        // The case this exists for. Both placements describe the same two files; only one of them
        // spells out a method.
        var (rows, left, right) = Build(
            "=class C {",
            "=    void A() {",
            "=        a();",
            "-    }",
            "-    void B() {",
            "-        b();",
            "=    }",
            "=}");

        var slid = Compact(rows, left, right);

        Assert.Equal(
        [
            "=class C {",
            "=    void A() {",
            "=        a();",
            "=    }",
            "-    void B() {",
            "-        b();",
            "-    }",
            "=}",
        ],
            Script(slid));
    }

    [Fact]
    public void Neither_document_changes()
    {
        var (rows, left, right) = Build(
            "=class C {",
            "=    void A() {",
            "=        a();",
            "-    }",
            "-    void B() {",
            "-        b();",
            "=    }",
            "=}");

        var (slidLeft, slidRight) = Documents(Compact(rows, left, right));

        Assert.Equal(left, slidLeft);
        Assert.Equal(right, slidRight);
    }

    [Fact]
    public void The_counts_do_not_change()
    {
        var (rows, left, right) = Build(
            "=class C {",
            "=    void A() {",
            "-    }",
            "-    void B() {",
            "=    }",
            "=}");

        var before = DiffResult.Create(rows);
        var after = DiffResult.Create([.. Compact(rows, left, right)]);

        Assert.Equal(before.Deleted, after.Deleted);
        Assert.Equal(before.Inserted, after.Inserted);
        Assert.Equal(before.Hunks.Count, after.Hunks.Count);
    }

    [Fact]
    public void An_inserted_group_slides_the_same_way()
    {
        var (rows, left, right) = Build(
            "=class C {",
            "=    void A() {",
            "=        a();",
            "+    }",
            "+    void B() {",
            "+        b();",
            "=    }",
            "=}");

        Assert.Equal(
        [
            "=class C {",
            "=    void A() {",
            "=        a();",
            "=    }",
            "+    void B() {",
            "+        b();",
            "+    }",
            "=}",
        ],
            Script(Compact(rows, left, right)));
    }

    [Fact]
    public void A_group_that_cannot_move_stays_put()
    {
        // Nothing at either boundary is equal to the line leaving the group, so there is no other
        // legal placement to consider.
        var (rows, left, right) = Build(
            "=    case 1:",
            "=        return One();",
            "+    case 2:",
            "+        return Two();",
            "=    case 3:",
            "=        return Three();");

        var slid = Compact(rows, left, right);

        Assert.Equal(Script(rows), Script(slid));
    }

    [Fact]
    public void A_document_with_nothing_to_move_comes_back_as_the_same_list()
    {
        // The allocation-free path: an unmoved diff must not pay for a copy of itself.
        var (rows, left, right) = Build("=a", "+b", "=c");

        Assert.Same(rows, Compact(rows, left, right));
    }

    [Fact]
    public void A_blank_line_boundary_is_preferred_to_an_indented_one()
    {
        // Both placements are legal - the group is bounded by identical blank lines - and the one
        // that ends against the blank line is the one that reads as a complete block.
        var (rows, left, right) = Build(
            "=body();",
            "=",
            "+    extra();",
            "+",
            "=    more();",
            "=");

        var slid = Compact(rows, left, right);

        // Whatever it picks, it must remain a valid diff of the same two documents.
        var (slidLeft, slidRight) = Documents(slid);
        Assert.Equal(left, slidLeft);
        Assert.Equal(right, slidRight);
    }

    [Fact]
    public void A_group_does_not_slide_across_an_ignored_row()
    {
        // An ignored row is drawn faintly precisely so the reader can see WHERE it is; moving a
        // change past one would move a difference across something they asked to keep track of.
        var rows = new List<DiffLine>
        {
            new(1, "x", 1, "x", ChangeKind.Unchanged),
            new(2, "}", null, null, ChangeKind.Deleted),
            new(3, "}", 2, "}", ChangeKind.Unchanged) { IsIgnored = true },
        };

        string[] left = ["x", "}", "}"];
        string[] right = ["x", "}"];

        Assert.Same(rows, Compact(rows, left, right));
    }

    [Fact]
    public void A_modified_row_is_never_a_group()
    {
        // It has a line on both sides, so there is no hole to slide - and pretending otherwise would
        // pair two lines the diff already said were different.
        var rows = new List<DiffLine>
        {
            new(1, "a", 1, "a", ChangeKind.Unchanged),
            new(2, "b", 2, "B", ChangeKind.Modified),
            new(3, "a", 3, "a", ChangeKind.Unchanged),
        };

        string[] left = ["a", "b", "a"];
        string[] right = ["a", "B", "a"];

        Assert.Same(rows, Compact(rows, left, right));
    }

    [Fact]
    public void Line_numbers_stay_monotonic_on_both_sides()
    {
        // The invariant the whole rendering depends on: each side's numbers must step through its own
        // document once, in order, skipping nothing.
        var (rows, left, right) = Build(
            "=class C {",
            "=    void A() {",
            "=        a();",
            "-    }",
            "-    void B() {",
            "-        b();",
            "=    }",
            "=}");

        var slid = Compact(rows, left, right);

        var expectedLeft = 1;
        var expectedRight = 1;

        foreach (var row in slid)
        {
            if (row.LeftNumber is { } l)
            {
                Assert.Equal(expectedLeft++, l);
            }

            if (row.RightNumber is { } r)
            {
                Assert.Equal(expectedRight++, r);
            }
        }

        Assert.Equal(left.Length + 1, expectedLeft);
        Assert.Equal(right.Length + 1, expectedRight);
    }

    [Fact]
    public void Equality_is_judged_on_the_keys_not_the_display_text()
    {
        // With "ignore case" on, two lines the user can see are different were matched as equal, and
        // the slider has to agree with that or it would refuse a move the diff already made.
        var rows = new List<DiffLine>
        {
            new(1, "start", 1, "start", ChangeKind.Unchanged),
            new(2, "END", null, null, ChangeKind.Deleted),
            new(3, "body", null, null, ChangeKind.Deleted),
            new(4, "end", 2, "end", ChangeKind.Unchanged),
        };

        string[] leftLines = ["start", "END", "body", "end"];
        string[] leftKeys = ["START", "END", "BODY", "END"];
        string[] rightLines = ["start", "end"];
        string[] rightKeys = ["START", "END"];

        var slid = ChangeGroupSlider.Compact(rows, leftKeys, leftLines, rightKeys, rightLines);

        // It moved, which it could only do by consulting the keys.
        Assert.NotSame(rows, slid);
        Assert.Equal("END", slid[1].LeftText);
        Assert.Equal(ChangeKind.Unchanged, slid[1].Kind);
    }
}
