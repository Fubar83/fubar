using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Reporting;
using Fubar.Diff.Core.Code;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Infrastructure.Code;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// The structural pass where it meets the rest of the pipeline: that it runs for C# and only for C#,
/// that it changes nothing about the text diff beside it, and that a comparison carries the answer far
/// enough for a report and a window to use it.
///
/// The matching itself is tested in <c>Fubar.Diff.Infrastructure.Tests.CodeStructureTests</c>, against
/// the parser that feeds it.
/// </summary>
public class CodeStructurePassTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FileComparisonService Build(bool withParser = true) => new(
        new Infrastructure.Files.TextFileReader(),
        new DiffPlexDiffEngine(),
        new DiffPlexInlineDiffEngine(),
        new TextLineNormalizer(),
        new JsonSemanticPass(new JsonAstParser()),
        structurePass: new CodeStructurePass(withParser ? new RoslynCodeStructureParser() : null));

    private static Task<FileComparison> Compare(
        string left,
        string right,
        string extension = ".cs",
        bool structure = true,
        bool withParser = true) =>
        Build(withParser).CompareTextAsync(
            left,
            right,
            new ComparisonOptions { Code = new CodeComparisonOptions { Structure = structure } },
            "left" + extension,
            "right" + extension,
            Token);

    private const string Left = """
        namespace Reporting;

        public class Report
        {
            public int Total()
            {
                return 0;
            }

            public void Print()
            {
                Console.WriteLine(Total());
            }
        }
        """;

    [Fact]
    public async Task A_CSharp_pair_gets_a_member_level_answer()
    {
        var right = Left.Replace("return 0;", "return 1;");

        var comparison = await Compare(Left, right);

        Assert.True(comparison.HasCodeStructure);
        Assert.Equal("Reporting.Report.Total()", Assert.Single(comparison.CodeChanges).Path);
        Assert.Equal(1, comparison.CodeSummary.Modified);
    }

    [Fact]
    public async Task The_text_diff_is_untouched_by_it()
    {
        // The rule that makes this safe to have on by default. A reformatted C# file IS different on
        // disk, a review is about those bytes, and quietly reporting them as equal would be the tool
        // lying about what it was shown - which is exactly what the JSON semantic pass IS allowed to
        // do, because two JSON documents in a different property order really are the same document.
        var right = Left.Replace("        return 0;", "            return 0;");

        var comparison = await Compare(Left, right);

        Assert.False(comparison.Result.AreIdentical);
        Assert.True(comparison.CodeSummary.NoFunctionalChange);
    }

    [Fact]
    public async Task Anything_that_is_not_source_it_can_read_gets_nothing_and_says_nothing()
    {
        var comparison = await Compare("a: 1", "a: 2", ".yaml");

        Assert.False(comparison.HasCodeStructure);
        Assert.Null(comparison.CodeStructureSkippedReason);
    }

    [Fact]
    public async Task Turning_it_off_skips_it_silently()
    {
        var right = Left.Replace("return 0;", "return 1;");

        var comparison = await Compare(Left, right, structure: false);

        Assert.False(comparison.HasCodeStructure);
        Assert.Null(comparison.CodeStructureSkippedReason);
    }

    [Fact]
    public async Task Without_a_parser_the_service_still_compares()
    {
        // The optional-adapter rule the binary reader follows too: a host that only wants text should
        // not have to supply a compiler front end, and going without one must degrade rather than
        // throw.
        var right = Left.Replace("return 0;", "return 1;");

        var comparison = await Compare(Left, right, withParser: false);

        Assert.False(comparison.HasCodeStructure);
        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task A_file_that_will_not_parse_at_all_says_why()
    {
        // Top-level statements produce no declarations, so there is no structure to compare - and the
        // reason is worth saying, unlike "this is not C#", which the user can see.
        var comparison = await Compare("Console.WriteLine(1);", "Console.WriteLine(2);");

        Assert.False(comparison.HasCodeStructure);
        Assert.NotNull(comparison.CodeStructureSkippedReason);
    }

    [Fact]
    public async Task Something_enormous_is_not_parsed()
    {
        var big = "public class Big {" + new string(' ', CodeStructurePass.MaxLength) + "}";

        var comparison = await Compare(big, big + " ");

        Assert.False(comparison.HasCodeStructure);
        Assert.Contains("too large", comparison.CodeStructureSkippedReason);
    }

    // ---- Reports ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_report_carries_the_headline()
    {
        // The field a CI gate wants: the line counts say a dozen lines differ, and this says whether
        // any of them matter.
        var right = Left.Replace("        return 0;", "            return 0;");

        var report = ComparisonReport.Build(await Compare(Left, right));

        Assert.True(report.CodeStructure.NoFunctionalChange);
        Assert.Contains("No functional changes", report.Summary());
    }

    [Fact]
    public async Task A_report_about_something_that_is_not_source_says_nothing_about_structure()
    {
        var report = ComparisonReport.Build(await Compare("a: 1", "a: 2", ".yaml"));

        Assert.Same(CodeStructureSummary.None, report.CodeStructure);
        Assert.DoesNotContain("functional", report.Summary());
    }

    [Fact]
    public async Task The_json_report_carries_the_counts()
    {
        var right = Left.Replace("return 0;", "return 1;");

        var json = ReportRenderer.Render(ComparisonReport.Build(await Compare(Left, right)), ReportFormat.Json);

        Assert.Contains("\"noFunctionalChange\": false", json);
        Assert.Contains("\"changed\": 1", json);
    }
}
