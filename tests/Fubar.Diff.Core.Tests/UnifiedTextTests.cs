using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Flattening a side-by-side comparison into one patch-style document.
///
/// This is the one place the codebase's "editor line i is DiffResult.Lines[i]" invariant deliberately
/// does not hold, so most of what needs proving is that the REPLACEMENT mapping is right: the hunks in
/// the document's own coordinates, and the row-by-row trail back to the comparison.
/// </summary>
public class UnifiedTextTests
{
    /// <summary>Rows from a compact script: "=x" context, "-x" removed, "+x" added, "~a|b" modified.</summary>
    private static DiffResult Result(params string[] script)
    {
        var rows = new List<DiffLine>();
        var left = 0;
        var right = 0;

        foreach (var entry in script)
        {
            var text = entry[1..];

            switch (entry[0])
            {
                case '=':
                    rows.Add(new DiffLine(++left, text, ++right, text, ChangeKind.Unchanged));
                    break;

                case '-':
                    rows.Add(new DiffLine(++left, text, null, null, ChangeKind.Deleted));
                    break;

                case '+':
                    rows.Add(new DiffLine(null, null, ++right, text, ChangeKind.Inserted));
                    break;

                case '~':
                    var parts = text.Split('|');
                    rows.Add(new DiffLine(++left, parts[0], ++right, parts[1], ChangeKind.Modified));
                    break;

                default:
                    throw new ArgumentException($"unknown marker '{entry[0]}'", nameof(script));
            }
        }

        return DiffResult.Create(rows);
    }

    private static string[] Lines(UnifiedDocument unified) => unified.Document.Text.Split('\n');

    [Fact]
    public void Context_appears_once_not_twice()
    {
        var unified = UnifiedText.Build(Result("=a", "=b"));

        Assert.Equal(["a", "b"], Lines(unified));
    }

    [Fact]
    public void A_modified_row_becomes_a_removal_and_an_addition()
    {
        var unified = UnifiedText.Build(Result("=a", "~old|new", "=b"));

        Assert.Equal(["a", "old", "new", "b"], Lines(unified));
        Assert.Equal(ChangeKind.Deleted, unified.Document.Lines[1].Kind);
        Assert.Equal(ChangeKind.Inserted, unified.Document.Lines[2].Kind);
    }

    [Fact]
    public void Removals_come_before_additions_within_a_hunk()
    {
        // What a patch looks like, and what anyone who reads diffs expects. Alternating line by line
        // reads better for a one-line edit and much worse for a block, which is the case needing help.
        var unified = UnifiedText.Build(Result("=a", "~one|ONE", "~two|TWO", "=b"));

        Assert.Equal(["a", "one", "two", "ONE", "TWO", "b"], Lines(unified));
    }

    [Fact]
    public void A_filler_contributes_nothing()
    {
        // It exists to keep two columns aligned. There is one column here.
        var rows = new List<DiffLine>
        {
            new(1, "a", 1, "a", ChangeKind.Unchanged),
            new(null, null, null, null, ChangeKind.Filler),
            new(2, "b", 2, "b", ChangeKind.Unchanged),
        };

        var unified = UnifiedText.Build(DiffResult.Create(rows));

        Assert.Equal(["a", "b"], Lines(unified));
    }

    [Fact]
    public void Hunks_are_reported_in_the_documents_own_row_indices()
    {
        // The comparison's hunk covers one row; the unified document's covers two, because the
        // modified row split. Navigation depends on this being the unified number.
        var result = Result("=a", "~old|new", "=b");
        var unified = UnifiedText.Build(result);

        var hunk = Assert.Single(unified.Hunks);
        Assert.Equal(1, hunk.StartIndex);
        Assert.Equal(2, hunk.EndIndex);
        Assert.Equal(result.Hunks.Count, unified.Hunks.Count);
    }

    [Fact]
    public void Every_unified_row_maps_back_to_the_row_it_came_from()
    {
        var result = Result("=a", "~old|new", "=b");
        var unified = UnifiedText.Build(result);

        Assert.Equal(unified.Document.Lines.Count, unified.SourceRows.Count);

        // Both halves of the split point at the same original row.
        Assert.Equal(0, unified.SourceRows[0]);
        Assert.Equal(1, unified.SourceRows[1]);
        Assert.Equal(1, unified.SourceRows[2]);
        Assert.Equal(2, unified.SourceRows[3]);
    }

    [Fact]
    public void A_hunks_rows_all_map_inside_that_hunk()
    {
        var result = Result("=a", "-x", "-y", "+p", "=b");
        var unified = UnifiedText.Build(result);

        var unifiedHunk = Assert.Single(unified.Hunks);
        var sourceHunk = Assert.Single(result.Hunks);

        for (var i = unifiedHunk.StartIndex; i <= unifiedHunk.EndIndex; i++)
        {
            var source = unified.SourceRows[i];
            Assert.InRange(source, sourceHunk.StartIndex, sourceHunk.EndIndex);
        }
    }

    [Fact]
    public void Line_numbers_come_from_the_side_the_line_came_from()
    {
        // One gutter column, so a removal shows its OLD number and an addition its NEW one - which is
        // what a unified diff has always done.
        var unified = UnifiedText.Build(Result("=a", "~old|new", "=b"));

        Assert.Equal(1, unified.Document.Lines[0].SourceNumber);
        Assert.Equal(2, unified.Document.Lines[1].SourceNumber);   // left line 2
        Assert.Equal(2, unified.Document.Lines[2].SourceNumber);   // right line 2
        Assert.Equal(3, unified.Document.Lines[3].SourceNumber);
    }

    [Fact]
    public void Character_spans_follow_the_side_they_were_computed_against()
    {
        var rows = new List<DiffLine>
        {
            new(1, "value = 1", 1, "value = 2", ChangeKind.Modified)
            {
                LeftSpans = [new CharSpan(8, 1, ChangeKind.Deleted)],
                RightSpans = [new CharSpan(8, 1, ChangeKind.Inserted)],
            },
        };

        var unified = UnifiedText.Build(DiffResult.Create(rows));

        Assert.Equal(8, Assert.Single(unified.Document.Lines[0].Spans).Start);
        Assert.Equal(8, Assert.Single(unified.Document.Lines[1].Spans).Start);
    }

    [Fact]
    public void An_ignored_row_stays_visible_and_stays_marked()
    {
        // It is context as far as hunks are concerned, but the faint band has to survive the
        // flattening or the user loses the only sign an ignore rule is doing anything.
        var rows = new List<DiffLine>
        {
            new(1, "a", 1, "A", ChangeKind.Unchanged) { IsIgnored = true },
        };

        var unified = UnifiedText.Build(DiffResult.Create(rows));

        Assert.True(unified.Document.Lines[0].IsIgnored);
    }

    [Fact]
    public void A_row_downgraded_to_one_sided_context_still_appears()
    {
        // CodeLineFilter turns an ignored insertion into Unchanged with a filler on the other side.
        // Preferring the right text would find null here, so the surviving side has to win.
        var rows = new List<DiffLine>
        {
            new(1, "// note", null, null, ChangeKind.Unchanged) { IsIgnored = true },
        };

        var unified = UnifiedText.Build(DiffResult.Create(rows));

        Assert.Equal(["// note"], Lines(unified));
    }

    [Fact]
    public void An_empty_comparison_produces_an_empty_document()
    {
        var unified = UnifiedText.Build(DiffResult.Empty);

        Assert.Equal(string.Empty, unified.Document.Text);
        Assert.Empty(unified.Hunks);
        Assert.Empty(unified.SourceRows);
    }

    [Fact]
    public void Every_changed_line_of_both_files_survives_the_flattening()
    {
        // The property that matters most: a unified view that quietly drops a line is worse than no
        // unified view, and nothing else here would catch it.
        var result = Result("=a", "-x", "+p", "~old|new", "=b", "-y");
        var unified = UnifiedText.Build(result);

        var expected = new List<string>();
        foreach (var row in result.Lines)
        {
            if (row.Kind == ChangeKind.Unchanged)
            {
                expected.Add(row.RightText ?? row.LeftText!);
            }
        }

        foreach (var row in result.Lines)
        {
            if (row.IsChange && row.LeftText is { } left)
            {
                Assert.Contains(left, Lines(unified));
            }

            if (row.IsChange && row.RightText is { } right)
            {
                Assert.Contains(right, Lines(unified));
            }
        }

        foreach (var context in expected)
        {
            Assert.Contains(context, Lines(unified));
        }
    }
}
