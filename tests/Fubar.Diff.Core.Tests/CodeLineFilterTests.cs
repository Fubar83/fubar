using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The post-alignment half of the code rules. The keys handle a comment that CHANGED; only this can
/// handle one that was ADDED, because an added line has nothing on the other side to be keyed against.
/// </summary>
public class CodeLineFilterTests
{
    private static readonly CodeComparisonOptions Comments = new() { IgnoreComments = true };
    private static readonly CodeComparisonOptions Blanks = new() { IgnoreBlankLines = true };

    private static CodeLines? Analyze(string[] lines, CodeComparisonOptions options) =>
        CodeLines.Analyze(lines, SourceLanguage.CSharp, options);

    [Fact]
    public void An_inserted_comment_line_becomes_ignored_context()
    {
        string[] left = ["foo();"];
        string[] right = ["// explain", "foo();"];

        var result = DiffResult.Create(
        [
            new DiffLine(null, null, 1, "// explain", ChangeKind.Inserted),
            new DiffLine(1, "foo();", 2, "foo();", ChangeKind.Unchanged),
        ]);

        var filtered = CodeLineFilter.Apply(result, Analyze(left, Comments), Analyze(right, Comments));

        Assert.Equal(ChangeKind.Unchanged, filtered.Lines[0].Kind);
        Assert.True(filtered.Lines[0].IsIgnored);
    }

    [Fact]
    public void An_ignored_row_forms_no_hunk()
    {
        // The distinction the whole IsIgnored design exists for: it can be DRAWN, but navigation, the
        // counts and the diff map must not stop on it.
        var result = DiffResult.Create(
        [
            new DiffLine(null, null, 1, "// explain", ChangeKind.Inserted),
        ]);

        var filtered = CodeLineFilter.Apply(result, Analyze([], Comments), Analyze(["// explain"], Comments));

        Assert.Empty(filtered.Hunks);
        Assert.True(filtered.AreIdentical);
        Assert.Equal(0, filtered.Inserted);
    }

    [Fact]
    public void An_inserted_code_line_is_left_alone()
    {
        var result = DiffResult.Create(
        [
            new DiffLine(null, null, 1, "foo();", ChangeKind.Inserted),
        ]);

        var filtered = CodeLineFilter.Apply(result, Analyze([], Comments), Analyze(["foo();"], Comments));

        Assert.Equal(ChangeKind.Inserted, filtered.Lines[0].Kind);
        Assert.Single(filtered.Hunks);
    }

    [Fact]
    public void A_deleted_blank_line_is_ignored_when_blank_lines_are()
    {
        var result = DiffResult.Create(
        [
            new DiffLine(1, "", null, null, ChangeKind.Deleted),
        ]);

        var filtered = CodeLineFilter.Apply(result, Analyze(["", "x();"], Blanks), Analyze(["x();"], Blanks));

        Assert.True(filtered.Lines[0].IsIgnored);
    }

    [Fact]
    public void A_modified_row_needs_both_sides_to_be_noise()
    {
        // A comment replaced by real code is a change, whatever the reader thinks of comments.
        string[] left = ["// was a note"];
        string[] right = ["realCode();"];

        var result = DiffResult.Create(
        [
            new DiffLine(1, left[0], 1, right[0], ChangeKind.Modified),
        ]);

        var filtered = CodeLineFilter.Apply(result, Analyze(left, Comments), Analyze(right, Comments));

        Assert.Equal(ChangeKind.Modified, filtered.Lines[0].Kind);
    }

    [Fact]
    public void The_filler_opposite_an_ignored_row_survives()
    {
        // Dropping the row would shorten one side and break the alignment invariant the whole
        // side-by-side view depends on.
        var result = DiffResult.Create(
        [
            new DiffLine(null, null, 1, "// explain", ChangeKind.Inserted),
            new DiffLine(1, "foo();", 2, "foo();", ChangeKind.Unchanged),
        ]);

        var filtered = CodeLineFilter.Apply(result, Analyze(["foo();"], Comments), Analyze(["// explain", "foo();"], Comments));

        Assert.Equal(2, filtered.Lines.Count);
        Assert.Null(filtered.Lines[0].LeftNumber);
    }

    [Fact]
    public void Spans_go_with_the_tint()
    {
        var result = DiffResult.Create(
        [
            new DiffLine(1, "// a", 1, "// b", ChangeKind.Modified)
            {
                LeftSpans = [new CharSpan(3, 1, ChangeKind.Deleted)],
                RightSpans = [new CharSpan(3, 1, ChangeKind.Inserted)],
            },
        ]);

        var filtered = CodeLineFilter.Apply(result, Analyze(["// a"], Comments), Analyze(["// b"], Comments));

        Assert.Empty(filtered.Lines[0].LeftSpans);
        Assert.Empty(filtered.Lines[0].RightSpans);
    }

    [Fact]
    public void A_result_with_nothing_to_filter_comes_back_unchanged()
    {
        var result = DiffResult.Create(
        [
            new DiffLine(1, "foo();", 1, "foo();", ChangeKind.Unchanged),
        ]);

        Assert.Same(result, CodeLineFilter.Apply(result, Analyze(["foo();"], Comments), Analyze(["foo();"], Comments)));
    }

    [Fact]
    public void No_analysis_on_either_side_is_a_no_op()
    {
        var result = DiffResult.Create(
        [
            new DiffLine(null, null, 1, "// explain", ChangeKind.Inserted),
        ]);

        Assert.Same(result, CodeLineFilter.Apply(result, null, null));
    }
}
