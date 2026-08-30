using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Patch;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Writing a comparison out as a unified diff.
///
/// The bar here is not "looks like a patch" but "applies as one", so most of these are about the hunk
/// headers - the line ranges are what a patch tool actually uses, and getting them wrong produces a
/// file that looks perfectly reasonable and fails to apply.
/// </summary>
public class UnifiedPatchTests
{
    /// <summary>Rows from a script: "=x" context, "-x" removed, "+x" added, "~a|b" modified.</summary>
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

                default:
                    var parts = text.Split('|');
                    rows.Add(new DiffLine(++left, parts[0], ++right, parts[1], ChangeKind.Modified));
                    break;
            }
        }

        return DiffResult.Create(rows);
    }

    private static string[] Patch(DiffResult result, int context = UnifiedPatch.DefaultContext) =>
        UnifiedPatch.Create(result, "a/file.txt", "b/file.txt", context)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void An_identical_comparison_produces_nothing()
    {
        // Not headers with no hunks: a patch file that applies nothing is something someone will try
        // to apply.
        Assert.Equal(string.Empty, UnifiedPatch.Create(Result("=a", "=b"), "a", "b"));
    }

    [Fact]
    public void The_headers_name_both_files()
    {
        var patch = Patch(Result("~old|new"));

        Assert.Equal("--- a/file.txt", patch[0]);
        Assert.Equal("+++ b/file.txt", patch[1]);
    }

    [Fact]
    public void A_modified_line_is_a_removal_then_an_addition()
    {
        var patch = Patch(Result("=one", "~old|new", "=two"));

        Assert.Equal(" one", patch[3]);
        Assert.Equal("-old", patch[4]);
        Assert.Equal("+new", patch[5]);
        Assert.Equal(" two", patch[6]);
    }

    [Fact]
    public void The_hunk_header_counts_lines_in_each_file()
    {
        // Three context lines above, one changed, three below: four old lines and four new.
        var patch = Patch(Result("=1", "=2", "=3", "~old|new", "=4", "=5", "=6"));

        Assert.Equal("@@ -1,7 +1,7 @@", patch[2]);
    }

    [Fact]
    public void An_insertion_makes_the_two_sides_different_lengths()
    {
        var patch = Patch(Result("=a", "+added", "=b"));

        Assert.Equal("@@ -1,2 +1,3 @@", patch[2]);
    }

    [Fact]
    public void A_deletion_makes_the_new_side_shorter()
    {
        var patch = Patch(Result("=a", "-gone", "=b"));

        Assert.Equal("@@ -1,3 +1,2 @@", patch[2]);
    }

    [Fact]
    public void Context_is_limited_to_the_lines_around_a_change()
    {
        // The whole point of a patch rather than the unified view: a 1,000-line file with one change
        // is seven lines of patch.
        var script = new List<string>();
        for (var i = 0; i < 50; i++)
        {
            script.Add("=line" + i);
        }

        script.Add("~old|new");

        var patch = Patch(Result([.. script]));

        // Two headers, one hunk header, three context, one removal, one addition.
        Assert.Equal(8, patch.Length);
        Assert.Equal("@@ -48,4 +48,4 @@", patch[2]);
    }

    [Fact]
    public void Changes_close_together_share_one_hunk()
    {
        // Their context overlaps, and emitting two hunks would describe the same lines twice - a patch
        // that does not apply.
        var patch = Patch(Result("~a|A", "=1", "=2", "~b|B"));

        Assert.Single(patch, line => line.StartsWith("@@", StringComparison.Ordinal));
    }

    [Fact]
    public void Changes_far_apart_get_their_own_hunks()
    {
        var script = new List<string> { "~a|A" };
        for (var i = 0; i < 20; i++)
        {
            script.Add("=line" + i);
        }

        script.Add("~b|B");

        var patch = Patch(Result([.. script]));

        Assert.Equal(2, patch.Count(line => line.StartsWith("@@", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_single_line_range_omits_the_count()
    {
        // What every other tool writes, and what a reader expects to see.
        var patch = Patch(Result("~old|new"), context: 0);

        Assert.Equal("@@ -1 +1 @@", patch[2]);
    }

    [Fact]
    public void Zero_context_emits_only_the_changes()
    {
        var patch = Patch(Result("=a", "~old|new", "=b"), context: 0);

        Assert.Equal(["--- a/file.txt", "+++ b/file.txt", "@@ -2 +2 @@", "-old", "+new"], patch);
    }

    [Fact]
    public void Line_numbers_follow_the_files_not_the_rows()
    {
        // Filler rows have no line number, so counting rows would drift by one per insertion - which
        // is a patch that applies in the wrong place.
        var patch = Patch(Result("=a", "+one", "+two", "=b", "~old|new"), context: 1);

        // The second hunk starts at old line 2 (a, b, old) and new line 4 (a, one, two, b, new).
        var headers = patch.Where(l => l.StartsWith("@@", StringComparison.Ordinal)).ToList();
        Assert.Contains("@@ -1,3 +1,5 @@", headers);
    }

    [Fact]
    public void Every_line_carries_a_marker()
    {
        // A line with no prefix is a malformed patch, and the failure is silent until someone applies
        // it.
        var patch = Patch(Result("=a", "~old|new", "-gone", "+added", "=b"));

        foreach (var line in patch.Skip(2))
        {
            Assert.True(
                line[0] is ' ' or '-' or '+' or '@',
                $"line '{line}' has no patch marker");
        }
    }

    [Fact]
    public void A_file_that_gained_everything_reports_an_empty_old_side()
    {
        // The convention for a created file: 0,0 means "this file has nothing here".
        var patch = Patch(Result("+one", "+two"));

        Assert.Equal("@@ -0,0 +1,2 @@", patch[2]);
    }
}
