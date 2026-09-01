using System;
using System.IO;
using System.Threading.Tasks;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Reporting;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Patch;

namespace Fubar.Diff.UI.Cli;

/// <summary>
/// Runs a comparison without a window and reports it through the process's exit code.
///
/// This is what makes the tool usable by something other than a person: a build step, a git hook, a
/// release check. The exit codes follow <c>diff</c> and <c>git diff --exit-code</c>, which is what a
/// script author will assume without reading anything - 0 means the same, 1 means different, and 2 is
/// reserved for "the question could not be answered", so a missing file can never be mistaken for a
/// clean result.
/// </summary>
public static class CliRunner
{
    public const int Same = 0;
    public const int Different = 1;
    public const int Failed = 2;

    /// <summary>
    /// Executes a parsed request. <paramref name="output"/> and <paramref name="error"/> are passed in
    /// rather than reached for, so this is testable without a console attached to anything.
    /// </summary>
    public static async Task<int> RunAsync(
        CliRequest request,
        IFileComparisonService comparisons,
        TextWriter output,
        TextWriter error)
    {
        if (request.ShowVersion)
        {
            output.WriteLine(Version());

            return Same;
        }

        if (request.Error is { } problem)
        {
            error.WriteLine(problem);
            error.WriteLine();
            error.WriteLine(CommandLine.Usage);

            return Failed;
        }

        if (request.ShowHelp || request.Left is not { } left || request.Right is not { } right)
        {
            output.WriteLine(CommandLine.Usage);

            return request.ShowHelp ? Same : Failed;
        }

        try
        {
            var comparison = await comparisons
                .CompareFilesAsync(left, right, request.Options)
                .ConfigureAwait(false);

            var report = ComparisonReport.Build(comparison, request.ContextLines);

            var toStandardOutput = request.ReportPath == "-";

            if (request.ReportPath is { } path)
            {
                WriteReport(report, comparison, request, path, output);
            }

            // Printed even when a report was written - a build log saying only "wrote diff.html" makes
            // the reader open a file to find out whether anything happened - but NOT when the report
            // itself went to standard output, where it would end up inside the patch being piped. The
            // summary goes to stderr there instead, so `--report - > changes.patch` produces a clean
            // patch and still tells the person watching what happened.
            if (!request.Quiet)
            {
                (toStandardOutput ? error : output).WriteLine(report.Summary());
            }

            // Format-only differences count as different, and this is the one place that is easy to
            // get wrong: the panes would show two identical documents, but the files are not
            // interchangeable on disk and a check that passed on them would be lying.
            return report.AreIdentical && report.FormatDifference is null ? Same : Different;
        }
        catch (TextFileReadException failure)
        {
            // The domain phrases these for a person already - "it is 92 MB, larger than the 64 MB
            // limit" - so they are passed through rather than wrapped in a second sentence.
            error.WriteLine(failure.Message);

            return Failed;
        }
        catch (IOException failure)
        {
            error.WriteLine(failure.Message);

            return Failed;
        }
        catch (UnauthorizedAccessException failure)
        {
            error.WriteLine(failure.Message);

            return Failed;
        }
    }

    /// <summary>
    /// Writes the report where it was asked for, or to standard output for <c>--report -</c> so it can
    /// be piped - which is the whole point of the patch format.
    /// </summary>
    private static void WriteReport(
        ComparisonReport report,
        FileComparison comparison,
        CliRequest request,
        string path,
        TextWriter output)
    {
        var toStandardOutput = path == "-";

        var format = request.ReportFormat
                     ?? (toStandardOutput ? ReportFormat.Text : ReportRenderer.FormatFor(path))
                     ?? ReportFormat.Text;

        // Built here rather than inside the renderer: a patch describes the WHOLE comparison, not the
        // context-trimmed summary a report holds, so it comes from the result directly.
        var patch = format == ReportFormat.Patch
            ? UnifiedPatch.Create(comparison.Result, "a/" + report.LeftPath, "b/" + report.RightPath)
            : null;

        var text = ReportRenderer.Render(report, format, patch);

        if (toStandardOutput)
        {
            output.WriteLine(text);

            return;
        }

        // A report is written even when the files match. "Nothing to report" is a result, and a CI job
        // that publishes an artifact unconditionally should not fail because the file is not there.
        File.WriteAllText(path, text);
    }

    private static string Version()
    {
        var assembly = typeof(CliRunner).Assembly;
        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);

        return informational.Length > 0
            ? $"Fubar Diff {((System.Reflection.AssemblyInformationalVersionAttribute)informational[0]).InformationalVersion}"
            : $"Fubar Diff {assembly.GetName().Version}";
    }
}
