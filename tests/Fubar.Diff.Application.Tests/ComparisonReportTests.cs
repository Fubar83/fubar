using System.Text.Json;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Reporting;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// Reducing a comparison to a report, and writing it out four ways.
///
/// A report is what leaves the program - into a build log, an artifact, a gate, a patch - so the
/// things worth pinning are the ones a reader would take at face value: that "identical" means it,
/// that context is context and not the whole file, and that a machine-readable format stays readable
/// by machines.
/// </summary>
public class ComparisonReportTests
{
    private sealed class Files(Dictionary<string, string[]> files) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextDocument(path, files[path], TextFormat.Default));
    }

    private static async Task<FileComparison> CompareAsync(
        string[] left,
        string[] right,
        ComparisonOptions? options = null,
        string leftName = "a.txt",
        string rightName = "b.txt")
    {
        var service = new FileComparisonService(
            new Files(new() { [leftName] = left, [rightName] = right }),
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        return await service.CompareFilesAsync(leftName, rightName, options ?? ComparisonOptions.Default);
    }

    [Fact]
    public async Task Identical_files_report_as_identical()
    {
        var report = ComparisonReport.Build(await CompareAsync(["a", "b"], ["a", "b"]));

        Assert.True(report.AreIdentical);
        Assert.Empty(report.Hunks);
        Assert.Contains("identical", report.Summary(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_counts_are_the_comparison_s_own()
    {
        var report = ComparisonReport.Build(await CompareAsync(["a", "gone", "c"], ["a", "c", "added"]));

        Assert.False(report.AreIdentical);
        Assert.Equal(1, report.Added);
        Assert.Equal(1, report.Removed);
    }

    [Fact]
    public async Task Context_is_context_rather_than_the_whole_file()
    {
        // A report of a one-line change in a thousand-line file is a few lines, not the file.
        var left = Enumerable.Range(1, 200).Select(i => $"line {i}").ToArray();
        var right = (string[])left.Clone();
        right[100] = "changed";

        var report = ComparisonReport.Build(await CompareAsync(left, right), contextLines: 2);

        var rows = Assert.Single(report.Hunks).Rows;
        Assert.Equal(5, rows.Count);
        Assert.Equal(ChangeKind.Modified, rows[2].Kind);
    }

    [Fact]
    public async Task No_context_is_only_the_changed_rows()
    {
        var report = ComparisonReport.Build(
            await CompareAsync(["a", "old", "c"], ["a", "new", "c"]),
            contextLines: 0);

        var row = Assert.Single(Assert.Single(report.Hunks).Rows);
        Assert.Equal("old", row.LeftText);
        Assert.Equal("new", row.RightText);
    }

    [Fact]
    public async Task Filler_rows_are_left_out()
    {
        // They exist to keep two editors row-aligned. A report is not two editors, and a blank line
        // that is in neither file would read as one that is in both.
        var report = ComparisonReport.Build(await CompareAsync(["a", "c"], ["a", "b", "c"]));

        Assert.All(
            Assert.Single(report.Hunks).Rows,
            row => Assert.NotEqual(ChangeKind.Filler, row.Kind));
    }

    [Fact]
    public async Task A_structural_comparison_says_how_many_structural_changes_there_were()
    {
        var report = ComparisonReport.Build(await CompareAsync(
            ["""{"a":1,"b":2}"""],
            ["""{"b":2,"a":9}"""],
            new ComparisonOptions { Mode = ComparisonMode.Json },
            "l.json",
            "r.json"));

        // Reordering is not a difference; the value that changed is.
        Assert.Equal(1, report.SemanticChanges);
    }

    [Fact]
    public async Task A_text_comparison_reports_no_structural_count_at_all()
    {
        // Null rather than zero, so a consumer can tell "nothing structural differs" from "structure
        // was never looked at".
        var report = ComparisonReport.Build(await CompareAsync(["a"], ["b"]));

        Assert.Null(report.SemanticChanges);
    }

    [Fact]
    public async Task An_ignored_path_stops_being_counted()
    {
        // Otherwise adding a rule changes nothing visible, which is exactly how a user concludes the
        // rule does not work.
        var options = new ComparisonOptions
        {
            Mode = ComparisonMode.Json,
            Json = new JsonComparisonOptions { IgnoredPaths = ["$.requestId"] },
        };

        var report = ComparisonReport.Build(await CompareAsync(
            ["""{"name":"widget","requestId":"abc"}"""],
            ["""{"name":"widget","requestId":"zzz"}"""],
            options,
            "l.json",
            "r.json"));

        Assert.Equal(0, report.SemanticChanges);
        Assert.True(report.AreIdentical);
    }

    // ---- Rendering -------------------------------------------------------------------------------

    [Fact]
    public async Task The_text_report_uses_diff_s_own_prefixes()
    {
        var report = ComparisonReport.Build(await CompareAsync(["a", "old", "c"], ["a", "new", "c"]));
        var text = ReportRenderer.Render(report, ReportFormat.Text);

        Assert.Contains("- old", text, StringComparison.Ordinal);
        Assert.Contains("+ new", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_json_report_parses_and_carries_the_verdict()
    {
        var report = ComparisonReport.Build(await CompareAsync(["a", "old"], ["a", "new"]));

        using var parsed = JsonDocument.Parse(ReportRenderer.Render(report, ReportFormat.Json));
        var root = parsed.RootElement;

        Assert.False(root.GetProperty("identical").GetBoolean());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("changes").GetInt32());
        Assert.Equal("a.txt", root.GetProperty("left").GetString());

        var row = root.GetProperty("changes")[0].GetProperty("rows")[1];
        Assert.Equal("modified", row.GetProperty("kind").GetString());
        Assert.Equal("old", row.GetProperty("left").GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_one_sided_row_omits_the_side_it_does_not_have()
    {
        var report = ComparisonReport.Build(await CompareAsync(["a"], ["a", "b"]), contextLines: 0);

        using var parsed = JsonDocument.Parse(ReportRenderer.Render(report, ReportFormat.Json));
        var row = parsed.RootElement.GetProperty("changes")[0].GetProperty("rows")[0];

        Assert.Equal("inserted", row.GetProperty("kind").GetString());
        Assert.False(row.TryGetProperty("left", out _));
        Assert.True(row.TryGetProperty("right", out _));
    }

    [Fact]
    public async Task The_html_report_escapes_the_content_it_shows()
    {
        // A diff of two HTML files must not become HTML.
        var report = ComparisonReport.Build(await CompareAsync(["<b>old</b>"], ["<i>new</i>"]));
        var html = ReportRenderer.Render(report, ReportFormat.Html);

        Assert.DoesNotContain("<b>old</b>", html, StringComparison.Ordinal);
        Assert.Contains("old", html, StringComparison.Ordinal);
        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_html_report_carries_its_own_styling()
    {
        // Self-contained: it gets attached to builds and opened years later, when whatever it might
        // have linked to is long gone.
        var html = ReportRenderer.Render(
            ComparisonReport.Build(await CompareAsync(["a"], ["b"])),
            ReportFormat.Html);

        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
    }
}
