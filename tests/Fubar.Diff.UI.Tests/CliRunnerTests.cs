using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Code;
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

    // ---- Functional changes only -------------------------------------------------------------

    private static async Task<(int Code, string Output)> RunCodeAsync(string[] left, string[] right, params string[] flags)
    {
        var service = new FileComparisonService(
            new Files(new() { ["l.cs"] = left, ["r.cs"] = right }),
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()),
            structurePass: new CodeStructurePass(new RoslynCodeStructureParser()));

        var output = new StringWriter();

        var code = await CliRunner.RunAsync(
            CommandLine.Parse([.. flags, "l.cs", "r.cs"]), service, output, new StringWriter());

        return (code, output.ToString());
    }

    private static readonly string[] Method =
    [
        "public class Report",
        "{",
        "    public int Total()",
        "    {",
        "        return 0;",
        "    }",
        "}",
    ];

    [Fact]
    public async Task The_summary_says_what_the_changed_lines_MEANT()
    {
        // The line counts are what the files did; this is the sentence a reviewer would otherwise
        // have to work out by reading every hunk.
        string[] reindented = [.. Method];
        reindented[4] = "            return 0;";

        var (code, output) = await RunCodeAsync(Method, reindented, "--check");

        Assert.Equal(CliRunner.Different, code);
        Assert.Contains("No functional changes", output);
    }

    [Fact]
    public async Task Functional_only_passes_a_file_that_was_merely_reformatted()
    {
        string[] reindented = [.. Method];
        reindented[4] = "            return 0;";

        var (code, _) = await RunCodeAsync(Method, reindented, "--functional", "-q");

        Assert.Equal(CliRunner.Same, code);
    }

    [Fact]
    public async Task Functional_only_still_fails_a_real_change()
    {
        string[] changed = [.. Method];
        changed[4] = "        return 1;";

        var (code, _) = await RunCodeAsync(Method, changed, "--functional", "-q");

        Assert.Equal(CliRunner.Different, code);
    }

    [Fact]
    public async Task Functional_only_falls_back_to_the_ordinary_answer_for_anything_it_cannot_read()
    {
        // A check that passed because the tool could not read the language would be the same lie as
        // one that passed on a changed file.
        var (code, _, _) = await RunAsync("--functional", "-q", "a.txt", "b.txt");

        Assert.Equal(CliRunner.Different, code);
    }

    [Fact]
    public void Functional_only_is_a_headless_flag()
    {
        // Otherwise it would open a window and never report anything.
        Assert.True(CommandLine.IsHeadless(["--functional", "a.cs", "b.cs"]));
    }
}
