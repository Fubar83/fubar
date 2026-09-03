using System.Reflection;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.Cli;

/// <summary>
/// Runs a collection from the command line and returns the process exit code.
///
/// <para><b>0 all passed, 1 something failed, 2 could not tell</b> - the same shape Fubar Diff uses, and
/// the one every script author already expects. "Could not tell" is kept strictly separate from
/// "failed" because a workspace that would not load and a collection whose assertions failed call for
/// completely different reactions from a build, and collapsing them would make the first look like the
/// second.</para>
/// </summary>
public static class CliRunner
{
    private const int Passed = 0;
    private const int Failed = 1;
    private const int CouldNotRun = 2;

    public static async Task<int> RunAsync(
        CliRequest request,
        ICollectionRunService runService,
        IWorkspaceStore workspaces,
        IRequestStore requests,
        IEnvironmentStore environments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (request.ShowHelp)
        {
            output.WriteLine(CommandLine.Usage);
            return Passed;
        }

        if (request.ShowVersion)
        {
            output.WriteLine(Version());
            return Passed;
        }

        if (request.Error is { } parseError)
        {
            error.WriteLine(parseError);
            error.WriteLine();
            error.WriteLine(CommandLine.Usage);
            return CouldNotRun;
        }

        if (request.Run is null)
        {
            error.WriteLine("Nothing to do. Use --run, or --help.");
            return CouldNotRun;
        }

        Workspace workspace;
        string root;
        try
        {
            root = ResolveWorkspaceRoot(request, workspaces)
                   ?? throw new InvalidOperationException(
                       "No workspace found. Pass --workspace, or run from inside one (a directory with a fubar.json).");

            workspace = await workspaces.LoadWorkspaceAsync(root, cancellationToken);
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return CouldNotRun;
        }

        WorkspaceEnvironment? environment = null;
        if (request.Environment is { } wanted)
        {
            var all = await environments.LoadEnvironmentsAsync(root, cancellationToken);
            environment = all.FirstOrDefault(e =>
                string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Id, wanted, StringComparison.OrdinalIgnoreCase));

            if (environment is null)
            {
                // Named and not found is an ERROR, never a quiet fall back to none: every {{variable}}
                // would resolve to nothing and the whole run would fail in a way that pointed at the
                // requests rather than at the typo.
                error.WriteLine($"No environment called \"{wanted}\". Available: {Names(all)}");
                return CouldNotRun;
            }
        }

        RunPlan plan;
        try
        {
            plan = BuildPlan(request, root, requests).Filtered(request.Filter);
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return CouldNotRun;
        }

        if (plan.IsEmpty)
        {
            // Exit 1, not 0. "Nothing matched, so it passed" is the failure mode every test runner has
            // had to grow out of, and it is reachable here with one typo in --filter.
            // "" is what --run with no path means; naming it as the workspace beats printing empty
            // quotes at someone trying to work out what went wrong.
            var where = string.IsNullOrEmpty(request.Run) ? "the workspace" : $"\"{request.Run}\"";
            error.WriteLine(request.Filter is { } filter
                ? $"Nothing to run: no request in {where} matches \"{filter}\"."
                : $"Nothing to run: {where} holds no requests.");
            return Failed;
        }

        var options = new RunOptions
        {
            StopOnFailure = request.StopOnFailure,
            DelayMilliseconds = request.DelayMilliseconds,
            // History is a record of what a PERSON sent. A CI run writing 200 entries per build into a
            // workspace's capped history would evict exactly that.
            RecordHistory = false,
        };

        // Written on the calling thread, NOT through Progress<T>. Progress<T> marshals to the captured
        // synchronization context, and a console process has none - so its callbacks go to the thread
        // pool, where a line can be printed out of order, interleaved with another, or after the summary
        // that is supposed to conclude them. The run is sequential, so writing inline is both correct and
        // ordered. (The GUI wants the opposite and uses Progress<T> for exactly the same reason.)
        var progress = request.Quiet
            ? null
            : new ImmediateProgress(update =>
            {
                if (update is { IsStarting: false, Report: { } step })
                {
                    output.WriteLine(Line(step));
                }
            });

        RunReport report;
        try
        {
            report = await runService.RunAsync(
                new CollectionRun(plan, workspace, environment, options), progress, cancellationToken);
        }
        catch (Exception ex)
        {
            error.WriteLine($"The run could not be completed: {ex.Message}");
            return CouldNotRun;
        }

        if (request.ReportPath is { } reportPath)
        {
            try
            {
                var text = CommandLine.ResolveFormat(request) switch
                {
                    RunReportFormat.JUnit => JUnitRunReport.Write(report),
                    _ => JsonRunReport.Write(report),
                };

                var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(reportPath, text, cancellationToken);
            }
            catch (Exception ex)
            {
                // The run itself already happened and its verdict stands. Report the write failure and
                // keep the verdict: turning a passing run into exit 2 because a path was not writable
                // would tell the build the wrong thing about the API.
                error.WriteLine($"Could not write the report: {ex.Message}");
            }
        }

        if (!request.Quiet)
        {
            output.WriteLine();
            output.WriteLine(report.Summary());

            foreach (var unexpected in report.UnexpectedStatuses)
            {
                // Said out loud, because it is the one thing that does not move the exit code and is
                // still usually worth a look.
                output.WriteLine(
                    $"  note: {unexpected.Step.Name} responded {unexpected.StatusCode} with no assertion to judge it.");
            }
        }

        return report.Ok ? Passed : Failed;
    }

    /// <summary>Reports on whichever thread called it. See the note where this is constructed.</summary>
    private sealed class ImmediateProgress(Action<RunProgress> onReport) : IProgress<RunProgress>
    {
        public void Report(RunProgress value) => onReport(value);
    }

    private static string Line(StepReport step)
    {
        var mark = step.Status switch
        {
            StepStatus.Passed when step.IsUnexpectedStatus && step.Assertions.Count == 0 => "!",
            StepStatus.Passed => "ok",
            StepStatus.Failed => "FAIL",
            StepStatus.Errored => "ERROR",
            _ => "skip",
        };

        var detail = step.Status == StepStatus.Errored
            ? step.Error ?? "no response"
            : $"{step.StatusCode} · {step.ElapsedMilliseconds:N0} ms";

        var line = $"{mark,-5} {step.Step.Order,3}. {step.Step.Name}  ({detail})";

        return step.AssertionsFailed == 0
            ? line
            : line + System.Environment.NewLine + string.Join(
                System.Environment.NewLine,
                step.Assertions.Where(a => !a.Passed)
                    .Select(a => a.Actual is { } actual ? $"        {a.Description} — got {actual}" : $"        {a.Description}"));
    }

    /// <summary>
    /// The workspace root: what was asked for, or the nearest ancestor of the run target holding a
    /// <c>fubar.json</c>. Walking up is what lets <c>--run ./collections/Orders</c> work from inside a
    /// checkout without naming the root as well.
    /// </summary>
    private static string? ResolveWorkspaceRoot(CliRequest request, IWorkspaceStore workspaces)
    {
        if (request.WorkspacePath is { } explicitPath)
        {
            var full = Path.GetFullPath(explicitPath);
            if (!workspaces.IsWorkspaceRoot(full))
            {
                throw new InvalidOperationException($"\"{full}\" is not a workspace (no fubar.json).");
            }

            return full;
        }

        var start = string.IsNullOrEmpty(request.Run)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(request.Run);

        // A file's own directory is where the walk starts; a directory starts at itself.
        var directory = File.Exists(start) ? Path.GetDirectoryName(start) : start;

        while (!string.IsNullOrEmpty(directory))
        {
            if (workspaces.IsWorkspaceRoot(directory))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static RunPlan BuildPlan(CliRequest request, string root, IRequestStore requests)
    {
        var tree = requests.BuildCollectionsTree(root);

        if (string.IsNullOrEmpty(request.Run))
        {
            return RunPlan.From(tree);
        }

        var target = Path.GetFullPath(
            Path.IsPathRooted(request.Run)
                ? request.Run
                : Path.Combine(root, "collections", request.Run));

        var node = Find(tree, target)
                   ?? throw new InvalidOperationException(
                       $"\"{request.Run}\" is not a folder or request in this workspace.");

        return RunPlan.From(node);
    }

    private static WorkspaceTreeNode? Find(IEnumerable<WorkspaceTreeNode> nodes, string fullPath)
    {
        foreach (var node in nodes)
        {
            // A request is stored as <name>.json and nobody types the extension, so --run Orders/Create
            // has to find Orders/Create.json. Matched both with and without it.
            if (PathMatches(node.FullPath, fullPath)
                || (!node.IsDirectory && PathMatches(WithoutExtension(node.FullPath), fullPath)))
            {
                return node;
            }

            if (Find(node.Children, fullPath) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string WithoutExtension(string path) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path));

    private static bool PathMatches(string? a, string b) =>
        a is not null
        && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);

    private static string Names(IReadOnlyList<WorkspaceEnvironment> environments) =>
        environments.Count == 0 ? "(none defined)" : string.Join(", ", environments.Select(e => e.Name));

    private static string Version() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
}
