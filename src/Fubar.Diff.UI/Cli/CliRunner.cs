using System;
using System.IO;
using System.Threading.Tasks;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Reporting;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Patch;
using Fubar.Diff.Core.Settings;

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
        TextWriter error,
        IProjectConfigStore? projectConfig = null)
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

        // The repository's own rules, if it has any. This matters more here than in the window: a
        // check that runs in CI is exactly where "our snapshots have a requestId that changes every
        // run" should be a fact about the repository rather than a flag every pipeline has to
        // remember to pass.
        var options = WithProjectRules(request.Options, left, right, projectConfig, error);

        try
        {
            var comparison = await comparisons
                .CompareFilesAsync(left, right, options)
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
            if (report.AreIdentical && report.FormatDifference is null)
            {
                return Same;
            }

            // --functional changes the QUESTION, from "do these files differ" to "did anything
            // meaningful change", and it can only be answered where the structural pass actually ran.
            // Where it did not, the answer falls through to the ordinary one rather than being guessed
            // at: a check that passed because the tool could not read the language would be the same
            // lie as one that passed on a changed file.
            return request.FunctionalOnly && report.CodeStructure.NoFunctionalChange ? Same : Different;
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
    /// Lays any <c>.fubardiff.json</c> rules over the options the command line asked for.
    ///
    /// Found from the LEFT file, falling back to the right - the two are usually in the same tree, and
    /// when they are not (a build output compared against a checked-in expectation) the one under
    /// version control is the one carrying the rules, which is almost always the left.
    ///
    /// A rule set that could not be read is reported to standard error and then ignored: a config with
    /// a trailing comma in it must not turn a comparison into a failure, but nor should it silently
    /// stop working.
    /// </summary>
    private static ComparisonOptions WithProjectRules(
        ComparisonOptions options,
        string left,
        string right,
        IProjectConfigStore? store,
        TextWriter error)
    {
        if (store is null)
        {
            return options;
        }

        var config = store.Find(left, out var problem) is { IsEmpty: false } found
            ? found
            : store.Find(right, out problem);

        if (problem is not null)
        {
            error.WriteLine(problem);
        }

        return config.For(right).ApplyTo(config.For(left).ApplyTo(options));
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
