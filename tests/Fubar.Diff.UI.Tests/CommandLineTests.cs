using Fubar.Diff.Application.Reporting;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.UI.Cli;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// Reading the command line, and deciding whether it is one at all.
///
/// The rule with real consequences is the second one: the same executable is a window and a batch
/// tool, and the arguments a difftool or mergetool configuration passes must keep opening a window.
/// Turning `FubarDiff a b` into a silent exit code would break every git integration in the world
/// with no error message to go on.
/// </summary>
public class CommandLineTests
{
    [Theory]
    [InlineData("--check", "a", "b")]
    [InlineData("--quiet", "a", "b")]
    [InlineData("-q", "a", "b")]
    [InlineData("--report", "out.html", "a")]
    [InlineData("--help")]
    [InlineData("--version")]
    public void A_flag_that_means_nothing_on_screen_runs_headless(params string[] args) =>
        Assert.True(CommandLine.IsHeadless(args));

    [Theory]
    [InlineData("a.txt", "b.txt")]
    [InlineData("--merge", "base.txt", "local.txt", "remote.txt")]
    [InlineData()]
    public void Everything_else_opens_a_window(params string[] args) =>
        Assert.False(CommandLine.IsHeadless(args));

    [Fact]
    public void Two_files_are_the_two_files()
    {
        var request = CommandLine.Parse(["--check", "left.txt", "right.txt"]);

        Assert.Null(request.Error);
        Assert.Equal("left.txt", request.Left);
        Assert.Equal("right.txt", request.Right);
    }

    [Fact]
    public void Comparison_flags_reach_the_comparison()
    {
        var request = CommandLine.Parse(
            ["--check", "-w", "-i", "--ignore-comments", "--mode", "json", "a", "b"]);

        Assert.Null(request.Error);
        Assert.True(request.Options.IgnoreWhitespace);
        Assert.True(request.Options.IgnoreCase);
        Assert.True(request.Options.Code.IgnoreComments);
        Assert.Equal(ComparisonMode.Json, request.Options.Mode);
    }

    [Fact]
    public void Ignore_path_can_be_given_more_than_once()
    {
        // The CI shape this exists for: two fields change on every run and neither is the point.
        var request = CommandLine.Parse(
            ["--check", "--ignore-path", "$.requestId", "--ignore-path", "$.timestamp", "a", "b"]);

        Assert.Equal(["$.requestId", "$.timestamp"], request.Options.Json.IgnoredPaths);
    }

    [Fact]
    public void The_report_format_comes_from_the_file_name()
    {
        // So --report out.html needs no second flag. The runner asks for this only when no explicit
        // format was given.
        Assert.Equal(ReportFormat.Html, ReportRenderer.FormatFor("out.html"));
        Assert.Equal(ReportFormat.Json, ReportRenderer.FormatFor("report.JSON"));
        Assert.Equal(ReportFormat.Patch, ReportRenderer.FormatFor("changes.patch"));
        Assert.Equal(ReportFormat.Text, ReportRenderer.FormatFor("summary.txt"));
        Assert.Null(ReportRenderer.FormatFor("mystery.bin"));
    }

    [Fact]
    public void An_explicit_format_is_kept()
    {
        var request = CommandLine.Parse(["--report", "out.bin", "--report-format", "patch", "a", "b"]);

        Assert.Equal(ReportFormat.Patch, request.ReportFormat);
    }

    [Theory]
    [InlineData("--report")]
    [InlineData("--report-format")]
    [InlineData("--mode")]
    [InlineData("--context")]
    [InlineData("--ignore-path")]
    public void A_flag_missing_its_value_says_so_rather_than_swallowing_the_next_file(string flag)
    {
        // Without this, `--mode a.txt b.txt` would quietly eat a file name as the mode and then
        // complain that only one file was given.
        var request = CommandLine.Parse(["--check", "a.txt", "b.txt", flag]);

        Assert.NotNull(request.Error);
        Assert.True(request.ShowHelp);
    }

    [Fact]
    public void An_unknown_option_is_an_error_rather_than_a_file_name()
    {
        var request = CommandLine.Parse(["--check", "--nonsense", "a", "b"]);

        Assert.Contains("--nonsense", request.Error);
    }

    [Fact]
    public void A_bad_enum_value_lists_the_ones_that_work()
    {
        var request = CommandLine.Parse(["--check", "--mode", "sideways", "a", "b"]);

        Assert.Contains("auto", request.Error);
    }

    [Theory]
    [InlineData(new[] { "--check", "only-one.txt" }, "Two files")]
    [InlineData(new[] { "--check", "a", "b", "c" }, "got 3")]
    public void The_wrong_number_of_files_is_refused(string[] args, string expected) =>
        Assert.Contains(expected, CommandLine.Parse(args).Error);

    [Fact]
    public void Help_beats_everything_else_on_the_line()
    {
        // Someone who typed --help alongside a half-remembered flag wants the help, not a complaint
        // about the other flag.
        var request = CommandLine.Parse(["--check", "--help", "--nonsense"]);

        Assert.True(request.ShowHelp);
        Assert.Null(request.Error);
    }
}
