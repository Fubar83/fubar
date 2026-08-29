using System.Diagnostics;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// Guardrails against a comparison becoming ACCIDENTALLY QUADRATIC.
///
/// Not benchmarks, and deliberately not written as any. The budgets below are enormous compared to
/// what these actually take - a 40,000-line comparison measured around 60 ms on a developer machine,
/// against a 15-second budget here - because a timing assertion tight enough to detect a 20% slowdown
/// is an assertion that fails on a busy CI agent, and a test that cries wolf gets deleted. What they
/// catch is the failure that actually matters and is easy to introduce without noticing: a nested scan
/// somewhere in the pipeline, which turns 60 ms into minutes and blows through any budget at all.
///
/// The shapes are chosen for that: long runs of IDENTICAL lines are what make an ambiguous change
/// group able to slide a long way, and repeated boilerplate is what gives an aligner the most pairs to
/// consider. If something here regresses to O(n²), these are where it shows first.
/// </summary>
public class PipelineScaleTests
{
    /// <summary>
    /// Generous enough to be immune to a loaded machine, tight enough that quadratic behaviour on
    /// these sizes cannot possibly fit inside it.
    /// </summary>
    private const int BudgetMs = 15_000;

    private const int Lines = 40_000;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class StubReader(Dictionary<string, string[]> files) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextDocument(path, files[path], TextFormat.Default));
    }

    private static FileComparisonService Service(Dictionary<string, string[]> files) => new(
        new StubReader(files),
        new DiffPlexDiffEngine(),
        new DiffPlexInlineDiffEngine(),
        new TextLineNormalizer(),
        new JsonSemanticPass(new JsonAstParser()));

    /// <summary>A plausible C# file, with the boilerplate density real code has.</summary>
    private static string[] SourceFile(int lines)
    {
        var result = new List<string>(lines);
        var method = 0;

        while (result.Count < lines)
        {
            result.Add($"    public void Method{method}(int value)");
            result.Add("    {");
            result.Add($"        if (value > {method})");
            result.Add("        {");
            result.Add($"            Log(\"method {method}\");");
            result.Add("        }");
            result.Add("    }");
            result.Add("");
            method++;
        }

        return [.. result.Take(lines)];
    }

    private static string[] EditedEvery(string[] source, int every)
    {
        var copy = (string[])source.Clone();
        for (var i = 0; i < copy.Length; i += every)
        {
            copy[i] += " // edited";
        }

        return copy;
    }

    private static async Task WithinBudget(string what, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        await action();
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < BudgetMs,
            $"{what} took {stopwatch.ElapsedMilliseconds} ms for {Lines:N0} lines, over the {BudgetMs} ms guardrail. "
            + "This budget is far above the real cost, so exceeding it means something became quadratic "
            + "rather than merely slower.");
    }

    [Fact]
    public async Task A_large_source_comparison_stays_linear()
    {
        var left = SourceFile(Lines);
        var right = EditedEvery(left, every: 40);
        var service = Service(new() { ["a.cs"] = left, ["b.cs"] = right });

        await WithinBudget("comparison", async () =>
            await service.CompareFilesAsync("a.cs", "b.cs", ComparisonOptions.Default, Token));
    }

    [Fact]
    public async Task The_code_rules_stay_linear_too()
    {
        // Scanning both documents for comments is an extra full pass over every character.
        var left = SourceFile(Lines);
        var right = EditedEvery(left, every: 40);
        var service = Service(new() { ["a.cs"] = left, ["b.cs"] = right });

        var options = new ComparisonOptions
        {
            Code = new CodeComparisonOptions { IgnoreComments = true, IgnoreBlankLines = true },
        };

        await WithinBudget("comparison with code rules", async () =>
            await service.CompareFilesAsync("a.cs", "b.cs", options, Token));
    }

    [Fact]
    public async Task A_file_of_near_identical_lines_does_not_blow_up()
    {
        // The slider's worst case by construction: every line is interchangeable with every other, so
        // an ambiguous group could in principle be offered every position in the file.
        var left = Enumerable.Repeat("    }", Lines).ToArray();
        var right = left.Take(Lines / 2).Concat(["    inserted();"]).Concat(left.Skip(Lines / 2)).ToArray();

        var service = Service(new() { ["a.cs"] = left, ["b.cs"] = right });

        await WithinBudget("comparison of identical lines", async () =>
            await service.CompareFilesAsync("a.cs", "b.cs", ComparisonOptions.Default, Token));
    }

    [Fact]
    public async Task A_large_three_way_merge_stays_linear()
    {
        // Two alignments plus the merge walk, and the walk's next-synchronisation search is the part
        // that would be quadratic if it ever restarted from the top.
        var ancestor = SourceFile(Lines);
        var left = EditedEvery(ancestor, every: 40);
        var right = EditedEvery(ancestor, every: 57);

        var service = new ThreeWayComparisonService(
            new StubReader(new() { ["b.cs"] = ancestor, ["l.cs"] = left, ["r.cs"] = right }),
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer());

        await WithinBudget("three-way merge", async () =>
            await service.CompareFilesAsync("b.cs", "l.cs", "r.cs", ComparisonOptions.Default, Token));
    }

    [Fact]
    public void Flattening_a_document_for_the_editors_stays_linear()
    {
        // Runs on every comparison, once per pane, on the UI thread's critical path.
        var lines = new List<DiffLine>(Lines);
        for (var i = 0; i < Lines; i++)
        {
            lines.Add(new DiffLine(i + 1, $"line {i}", i + 1, $"line {i}", ChangeKind.Unchanged));
        }

        var result = DiffResult.Create(lines);

        var stopwatch = Stopwatch.StartNew();
        AlignedText.Build(result, DiffSide.Left);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < BudgetMs, $"flattening took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Scanning_a_large_source_file_stays_linear()
    {
        // The lexer walks every character once and looks up operators per punctuation character; a
        // mistake in either loop is quadratic in line length rather than in line count.
        var lines = SourceFile(Lines);

        var stopwatch = Stopwatch.StartNew();
        SourceScanner.Scan(lines, SourceLanguage.CSharp);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < BudgetMs, $"scanning took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_single_enormous_line_does_not_blow_up()
    {
        // Minified JSON and bundled JavaScript are one line of megabytes. Anything in the scanner that
        // is quadratic in LINE LENGTH rather than line count shows up here and nowhere else.
        var huge = string.Join(',', Enumerable.Range(0, 200_000).Select(i => $"\"k{i}\":{i}"));

        var stopwatch = Stopwatch.StartNew();
        SourceScanner.Scan(["{" + huge + "}"], SourceLanguage.JavaScript);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < BudgetMs, $"scanning one huge line took {stopwatch.ElapsedMilliseconds} ms");
    }
}
