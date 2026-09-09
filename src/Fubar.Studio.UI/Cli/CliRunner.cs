using System.Reflection;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Snapshots;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Variables;

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
/// <param name="Snapshots">Where recorded snapshots are read from, for <c>--oracle snapshot</c>.</param>
/// <param name="Recording">What writes them, for <c>--update-snapshots</c>.</param>
/// <param name="Batches">What expands <c>@name</c>.</param>
/// <remarks>
/// One record rather than eleven parameters, and every field REQUIRED. An optional service here reads
/// as "this feature is unavailable in some builds", which is how a flag comes to parse, print nothing,
/// and do nothing - which is exactly what happened to --oracle and --update-snapshots between the
/// commit that added them and the commit that added this.
/// </remarks>
public sealed record CliServices(
    ICollectionRunService Runner,
    IWorkspaceStore Workspaces,
    IRequestStore Requests,
    IEnvironmentStore Environments,
    ISnapshotStore Snapshots,
    ISnapshotRecordingService Recording,
    IBatchPlanner Batches,
    ExternalVariableSource? ExternalVariables = null);

public static class CliRunner
{
    private const int Passed = 0;
    private const int Failed = 1;
    private const int CouldNotRun = 2;

    public static async Task<int> RunAsync(
        CliRequest request,
        CliServices services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var runService = services.Runner;
        var workspaces = services.Workspaces;
        var requests = services.Requests;
        var environments = services.Environments;
        var externalVariables = services.ExternalVariables;

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

        if (request.Run is null && !request.Validate)
        {
            error.WriteLine("Nothing to do. Use --run, --validate, or --help.");
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

        if (request.Validate)
        {
            return ValidateWorkspace(request, root, output, error);
        }

        IReadOnlyList<WorkspaceEnvironment>? loadedEnvironments = null;

        // Named and not found is an ERROR, never a quiet fall back to none: every {{variable}} would
        // resolve to nothing and the whole run would fail in a way that pointed at the requests
        // rather than at the typo.
        async Task<(WorkspaceEnvironment? Found, string? Error)> FindEnvironmentAsync(string wanted)
        {
            loadedEnvironments ??= await environments.LoadEnvironmentsAsync(root, cancellationToken);

            var found = loadedEnvironments.FirstOrDefault(e =>
                string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Id, wanted, StringComparison.OrdinalIgnoreCase));

            return found is null
                ? (null, $"No environment called \"{wanted}\". Available: {Names(loadedEnvironments)}")
                : (found, null);
        }

        WorkspaceEnvironment? environment = null;
        if (request.Environment is { } wanted)
        {
            var (found, message) = await FindEnvironmentAsync(wanted);
            if (message is not null)
            {
                error.WriteLine(message);
                return CouldNotRun;
            }

            environment = found;
        }

        // Variables supplied from outside the workspace, loaded once the rest of the command line is
        // known to be good. This is how a pipeline supplies a secret at all: a build agent has no OS
        // keyring, so before this a Secret variable resolved to nothing and the literal {{token}} went
        // out over the wire as text.
        if (externalVariables is not null)
        {
            IReadOnlyList<string> envFileLines = [];

            if (request.EnvFilePath is { } envFilePath)
            {
                try
                {
                    envFileLines = await File.ReadAllLinesAsync(envFilePath, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Named and unreadable is "could not tell", never a quiet run without it: every
                    // {{variable}} it was meant to supply would resolve to nothing and the failure
                    // would point at the requests instead of at the path.
                    error.WriteLine($"Could not read --env-file \"{envFilePath}\": {ex.Message}");
                    return CouldNotRun;
                }
            }

            externalVariables.LoadFrom(ExternalVariableSource.Build(
                environment: null,
                envFileLines: envFileLines,
                varFlags: request.Vars));
        }

        RunPlan plan;
        Batch? batch = null;
        try
        {
            var selector = RunSelector.Parse(request.Run);

            if (selector.Kind == RunSelectorKind.Batch)
            {
                var resolved = await services.Batches
                    .ExpandAsync(workspace, selector.BatchName!, selector.BatchOwnerPath, cancellationToken)
                    .ConfigureAwait(false);

                batch = resolved.Batch;
                plan = resolved.Plan;
            }
            else
            {
                plan = TreeLookup.Expand(requests.BuildCollectionsTree(root), selector);
            }

            plan = plan.Filtered(request.Filter);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
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

        // A batch states the environment it is about. The command line still wins - a batch written
        // for staging has to be runnable against a branch deployment without editing the file.
        if (environment is null && batch?.Environments is [{ Length: > 0 } batchEnvironment, ..])
        {
            var (found, message) = await FindEnvironmentAsync(batchEnvironment);
            if (message is not null)
            {
                error.WriteLine($"This batch names an environment that is not here. {message}");
                return CouldNotRun;
            }

            environment = found;
        }

        var options = new RunOptions
        {
            StopOnFailure = request.StopOnFailure || (batch?.Options?.StopOnFailure ?? false),
            DelayMilliseconds = request.DelayMilliseconds != 0
                ? request.DelayMilliseconds
                : batch?.Options?.DelayMs ?? 0,
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

        // Recording is a deliberate act, and it is the opposite of comparing - so it is its own branch
        // rather than a flag inside the run. The parser has already refused asking for both.
        if (request.UpdateSnapshots)
        {
            return await RecordAsync(
                request, services, new SnapshotRecording(
                    plan,
                    workspace,
                    environment,
                    request.SharedSnapshots ? SnapshotScope.Shared : SnapshotScope.Environment,
                    options with { CaptureResponseBodies = true }),
                progress, output, error, cancellationToken);
        }

        IOracle oracle;
        try
        {
            oracle = await ResolveOracleAsync(request, batch, services, FindEnvironmentAsync);
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine(ex.Message);
            return CouldNotRun;
        }

        // Bodies are only kept when something is going to compare them: a run of thirty requests would
        // otherwise hold thirty response bodies for as long as its report is alive.
        if (oracle.Kind != OracleKind.None)
        {
            options = options with { CaptureResponseBodies = true };
        }

        RunReport report;
        try
        {
            report = await runService.RunAsync(
                new CollectionRun(plan, workspace, environment, options, oracle, batch?.Overlay),
                progress,
                cancellationToken);
        }
        catch (Exception ex)
        {
            error.WriteLine($"The run could not be completed: {ex.Message}");
            return CouldNotRun;
        }

        // Written before the report, so a run that is being audited is recorded even if the report
        // path turns out to be unwritable. A failure here never changes the verdict: the run already
        // happened, and telling the build the API is broken because a log file could not be appended
        // to would be the wrong answer about the wrong thing.
        if (request.AuditLogPath is { } auditPath
            && RunAuditLog.Append(auditPath, report, workspace, environment) is { } auditError)
        {
            error.WriteLine($"Could not write the audit log \"{auditPath}\": {auditError}");
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
                    $"  note: {unexpected.Step.QualifiedName} responded {unexpected.StatusCode} with no assertion to judge it.");
            }

            // A rule that silently did nothing reads as a check. Printed even on a green run, because
            // that is exactly the run where an unapplied tolerance is invisible.
            foreach (var step in report.Steps)
            {
                foreach (var comparisonWarning in step.ComparisonWarnings)
                {
                    output.WriteLine($"  note: {step.Step.QualifiedName}: {comparisonWarning}");
                }
            }
        }

        return report.Ok ? Passed : Failed;
    }

    /// <summary>
    /// What judges each response: the command line, then the batch's own choice, then nothing.
    /// </summary>
    /// <remarks>
    /// The command line wins so a batch written to compare against snapshots can still be run once
    /// with <c>--oracle none</c> to see what it actually returns - which is the first thing anybody
    /// does when a snapshot run starts failing.
    /// </remarks>
    private static async Task<IOracle> ResolveOracleAsync(
        CliRequest request,
        Batch? batch,
        CliServices services,
        Func<string, Task<(WorkspaceEnvironment? Found, string? Error)>> findEnvironment)
    {
        var kind = request.Oracle?.Kind
                   ?? batch?.Oracle?.Kind switch
                   {
                       BatchOracleKind.Snapshot => CliOracleKind.Snapshot,
                       BatchOracleKind.Environment => CliOracleKind.Environment,
                       _ => CliOracleKind.None,
                   };

        if (kind == CliOracleKind.Snapshot)
        {
            return new SnapshotOracle(services.Snapshots);
        }

        if (kind != CliOracleKind.Environment)
        {
            return NoOracle.Instance;
        }

        // The second environment: from --oracle env:NAME, else from the batch's second entry.
        var named = request.Oracle?.Environment
                    ?? batch?.Oracle?.Environment
                    ?? (batch?.Environments is [_, { Length: > 0 } second, ..] ? second : null)
                    ?? throw new InvalidOperationException(
                        "An environment comparison needs a second environment. Use --oracle env:<name>.");

        var (found, message) = await findEnvironment(named);

        return found is null
            ? throw new InvalidOperationException(message!)
            : new EnvironmentOracle(services.Runner, found);
    }

    /// <summary>
    /// Records what the plan returns, and reports what was written and what was not.
    /// </summary>
    /// <remarks>
    /// Exit code by the same rule as a run: a step that could not be sent has nothing to record, and
    /// reporting success for it would leave the next comparison run failing against a snapshot nobody
    /// knew was missing.
    /// </remarks>
    private static async Task<int> RecordAsync(
        CliRequest request,
        CliServices services,
        SnapshotRecording recording,
        IProgress<RunProgress>? progress,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        SnapshotRecordingReport report;
        try
        {
            report = await services.Recording.RecordAsync(recording, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            error.WriteLine($"The snapshots could not be recorded: {ex.Message}");
            return CouldNotRun;
        }

        foreach (var warning in report.Warnings)
        {
            error.WriteLine($"  warning: {warning}");
        }

        if (!request.Quiet)
        {
            var scope = recording.Scope == SnapshotScope.Shared
                ? "shared across environments"
                : $"for {recording.Environment?.Name ?? "no environment"}";

            output.WriteLine();
            output.WriteLine($"Recorded {report.Written.Count} snapshot(s) {scope}.");
        }

        return report.Run.Errored == 0 && report.Warnings.Count == 0 ? Passed : Failed;
    }

    /// <summary>Reports on whichever thread called it. See the note where this is constructed.</summary>
    private sealed class ImmediateProgress(Action<RunProgress> onReport) : IProgress<RunProgress>
    {
        public void Report(RunProgress value) => onReport(value);
    }

    /// <summary>
    /// One line per step.
    /// </summary>
    /// <remarks>
    /// The comparison verdict is on the SAME line and beats the request's own status, because it is
    /// the answer the run was asked for. Reading only <c>Status</c> printed "ok" against every row of
    /// a run whose summary then said "1 differ" - which is a report that contradicts itself and sends
    /// the reader to the wrong place.
    /// </remarks>
    private static string Line(StepReport step)
    {
        var mark = step switch
        {
            // Cleanup never fails a run, so it never prints as a failure - "FAIL" on a row of a run
            // reported as passed is a report arguing with itself. What went wrong is still printed
            // underneath; only the word changes.
            { Step.IsTeardown: true, Status: StepStatus.Failed or StepStatus.Errored } => "WARN",

            { Status: StepStatus.Errored } => "ERROR",
            { Status: StepStatus.Failed } => "FAIL",
            { Comparison: ComparisonVerdict.Differs } => "DIFF",
            { Comparison: ComparisonVerdict.Unavailable } => "?",
            { Status: StepStatus.Passed, IsUnexpectedStatus: true, Assertions.Count: 0 } => "!",
            { Status: StepStatus.Passed } => "ok",
            _ => "skip",
        };

        var detail = step switch
        {
            { Status: StepStatus.Errored } => step.Error ?? "no response",

            { Comparison: ComparisonVerdict.Differs } =>
                $"{step.DifferenceCount} difference{(step.DifferenceCount == 1 ? "" : "s")} from {step.ComparedAgainst}",

            { Comparison: ComparisonVerdict.Unavailable } =>
                step.ComparisonUnavailableReason ?? "nothing to compare against",

            // Which side it agreed with, because a run that quietly switched from the shared snapshot
            // to a per-environment one is a run whose green means something different.
            { Comparison: ComparisonVerdict.Same } =>
                $"{step.StatusCode} · {step.ElapsedMilliseconds:N0} ms · matches {step.ComparedAgainst}"
                + (step.ToleratedCount > 0 ? $" ({step.ToleratedCount} within tolerance)" : ""),

            _ => $"{step.StatusCode} · {step.ElapsedMilliseconds:N0} ms",
        };

        // Marked, because a cleanup row that failed is not a failing test and a reader scanning a red
        // run has to be able to tell the two apart at a glance.
        var name = step.Step.IsTeardown
            ? $"{step.Step.QualifiedName}  [cleanup]"
            : step.Step.QualifiedName;

        var line = $"{mark,-5} {step.Step.Order,3}. {name}  ({detail})";

        var notes = new List<string>();

        notes.AddRange(step.Assertions.Where(a => !a.Passed)
            .Select(a => a.Actual is { } actual ? $"        {a.Description} — got {actual}" : $"        {a.Description}"));

        // A capture that could not be applied does not fail its own step - the request answered, and
        // whether a missing field matters is what an assertion is for. It is printed HERE because the
        // failure it causes usually lands several steps later as a {{variable}} that never resolved,
        // and without this the step that actually caused it reads "ok".
        notes.AddRange(step.Captures.Where(c => !c.Ok)
            .Select(c => $"        could not capture {{{{{c.VariableName}}}}}: {c.Error ?? "no match"}"));

        return notes.Count == 0
            ? line
            : line + System.Environment.NewLine + string.Join(System.Environment.NewLine, notes);
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


    /// <summary>
    /// Checks every workspace file against its schema, with the same exit contract as a run.
    ///
    /// <para>Problems are printed one per line as <c>path:pointer: message</c> - the shape a build log
    /// and an editor's problem list both already parse - and warnings are labelled rather than folded
    /// in, because failing someone's build over a deliberately public sandbox key would be wrong about
    /// the case they understand better than this does. <c>--strict</c> is how a team opts into that.</para>
    /// </summary>
    private static int ValidateWorkspace(CliRequest request, string root, TextWriter output, TextWriter error)
    {
        IReadOnlyList<Fubar.Studio.Infrastructure.Workspaces.ValidationProblem> problems;
        try
        {
            problems = new Fubar.Studio.Infrastructure.Workspaces.WorkspaceValidator().Validate(root);
        }
        catch (Exception ex)
        {
            error.WriteLine($"The workspace could not be validated: {ex.Message}");
            return CouldNotRun;
        }

        var errors = problems.Count(p => p.IsError);
        var warnings = problems.Count - errors;

        if (!request.Quiet)
        {
            foreach (var problem in problems)
            {
                var where = string.IsNullOrEmpty(problem.Location) ? "" : problem.Location;
                var line = $"{problem.Path}:{where}: {(problem.IsError ? "error" : "warning")}: {problem.Message}";

                (problem.IsError ? error : output).WriteLine(line);
            }

            output.WriteLine(problems.Count == 0
                ? "Workspace is valid."
                : $"{errors} error(s), {warnings} warning(s).");
        }

        return errors > 0 || (request.Strict && warnings > 0) ? Failed : Passed;
    }

    private static string Names(IReadOnlyList<WorkspaceEnvironment> environments) =>
        environments.Count == 0 ? "(none defined)" : string.Join(", ", environments.Select(e => e.Name));

    private static string Version() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
}
