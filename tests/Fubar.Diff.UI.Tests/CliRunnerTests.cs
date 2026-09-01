using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.UI.Cli;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// The headless run, and above all its exit code.
///
/// The exit code is the entire interface for a build step, and its meanings follow diff and
/// `git diff --exit-code` because that is what a script author will assume without reading anything:
/// 0 the same, 1 different, 2 the question could not be answered. The third is the one worth being
/// careful about - a missing file must never come back as a clean result.
/// </summary>
public class CliRunnerTests
{
    private sealed class Files(Dictionary<string, string[]> files) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            files.TryGetValue(path, out var lines)
                ? Task.FromResult(new TextDocument(path, lines, TextFormat.Default))
                : throw new TextFileReadException(path, "the file does not exist.");
    }

    private static FileComparisonService Service(Dictionary<string, string[]> files) => new(
        new Files(files),
        new DiffPlexDiffEngine(),
        new DiffPlexInlineDiffEngine(),
        new TextLineNormalizer(),
        new JsonSemanticPass(new JsonAstParser()));

    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        var service = Service(new()
        {
            ["same-a.txt"] = ["alpha", "beta"],
            ["same-b.txt"] = ["alpha", "beta"],
            ["a.txt"] = ["alpha", "beta"],
            ["b.txt"] = ["alpha", "BETA", "gamma"],
        });

        var output = new StringWriter();
        var error = new StringWriter();

        var code = await CliRunner.RunAsync(CommandLine.Parse(args), service, output, error);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Identical_files_exit_zero()
    {
        var (code, output, _) = await RunAsync("--check", "same-a.txt", "same-b.txt");

        Assert.Equal(CliRunner.Same, code);
        Assert.Contains("identical", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Differing_files_exit_one()
    {
        var (code, output, _) = await RunAsync("--check", "a.txt", "b.txt");

        Assert.Equal(CliRunner.Different, code);
        Assert.Contains("change(s)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_exits_two_and_says_why()
    {
        // Not 1. A gate that treats "could not open the file" as "they differ" is wrong in the safe
        // direction; one that treats it as "they match" is wrong in the dangerous one, and the way to
        // never be either is a code of its own.
        var (code, output, error) = await RunAsync("--check", "missing.txt", "b.txt");

        Assert.Equal(CliRunner.Failed, code);
        Assert.Contains("does not exist", error, StringComparison.Ordinal);
        Assert.Empty(output);
    }

    [Fact]
    public async Task Quiet_says_nothing_at_all()
    {
        var (code, output, error) = await RunAsync("-q", "a.txt", "b.txt");

        Assert.Equal(CliRunner.Different, code);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task A_report_on_standard_output_keeps_the_summary_off_it()
    {
        // `--report - --report-format patch > changes.patch` has to produce a patch, not a patch with
        // a sentence on the end. The summary still gets written, to the other stream.
        var (_, output, error) = await RunAsync("--report", "-", "--report-format", "patch", "a.txt", "b.txt");

        Assert.StartsWith("--- a/a.txt", output, StringComparison.Ordinal);
        Assert.DoesNotContain("change(s)", output, StringComparison.Ordinal);
        Assert.Contains("change(s)", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_exits_zero()
    {
        // Asking for help is not a failure, and a wrapper script that checks the exit code should not
        // think it was.
        var (code, output, _) = await RunAsync("--help");

        Assert.Equal(CliRunner.Same, code);
        Assert.Contains("Exit codes", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_command_line_exits_two_with_the_usage()
    {
        var (code, _, error) = await RunAsync("--check", "--nonsense", "a.txt", "b.txt");

        Assert.Equal(CliRunner.Failed, code);
        Assert.Contains("--nonsense", error, StringComparison.Ordinal);
        Assert.Contains("Exit codes", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_exits_zero_and_names_the_app()
    {
        var (code, output, _) = await RunAsync("--version");

        Assert.Equal(CliRunner.Same, code);
        Assert.Contains("Fubar Diff", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ignoring_the_paths_that_always_change_can_make_a_comparison_clean()
    {
        // The workflow the whole headless mode exists for: two API responses, or two snapshots, whose
        // only differences are the fields nobody is asserting on.
        var service = Service(new()
        {
            ["l.json"] = ["""{"name":"widget","requestId":"abc-1"}"""],
            ["r.json"] = ["""{"requestId":"zzz-9","name":"widget"}"""],
        });

        var output = new StringWriter();

        var code = await CliRunner.RunAsync(
            CommandLine.Parse(["--check", "--ignore-path", "$.requestId", "l.json", "r.json"]),
            service,
            output,
            new StringWriter());

        Assert.Equal(CliRunner.Same, code);
    }
}
