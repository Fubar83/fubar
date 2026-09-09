using Fubar.Studio.Core.Snapshots;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// The Run window: a plan, the options that shape it, and the report it produces.
///
/// <para>The whole plan is listed before anything is sent, with every row pending. A window that filled
/// in a row at a time would be unable to answer "how much is left?" until it had finished, and the
/// commonest reason to look at a running collection is to decide whether to wait for it.</para>
/// </summary>
public sealed partial class CollectionRunViewModel : ViewModelBase
{
    private readonly ICollectionRunService _runService;
    private readonly ISnapshotRecordingService _recording;
    private readonly ISnapshotStore _snapshots;
    private readonly Workspace _workspace;
    private readonly WorkspaceEnvironment? _environment;
    private readonly RunPlan _fullPlan;
    private readonly Batch? _batch;
    private readonly Services.IDiffPreviewService _diffPreview;
    private readonly Services.IComparisonSettingsContext _settingsContext;
    private CancellationTokenSource? _cancellation;

    public CollectionRunViewModel(
        ICollectionRunService runService,
        ISnapshotRecordingService recording,
        ISnapshotStore snapshots,
        RunPlan plan,
        Workspace workspace,
        WorkspaceEnvironment? environment,
        IReadOnlyList<WorkspaceEnvironment> allEnvironments,
        string target,
        Services.IDiffPreviewService diffPreview,
        Services.IComparisonSettingsContext settingsContext,
        Batch? batch = null)
    {
        ArgumentNullException.ThrowIfNull(allEnvironments);

        _runService = runService;
        _recording = recording;
        _snapshots = snapshots;
        _fullPlan = plan;
        _workspace = workspace;
        _batch = batch;
        _diffPreview = diffPreview;
        _settingsContext = settingsContext;

        // A batch that names an environment is run against it, the way the command line does - a batch
        // written for staging that silently went to whatever was selected in the toolbar would be a
        // regression suite pointed at the wrong system, and the header here says which it used.
        _environment = batch?.Environments is [{ Length: > 0 } named, ..]
            ? allEnvironments.FirstOrDefault(
                  e => string.Equals(e.Name, named, StringComparison.OrdinalIgnoreCase)) ?? environment
            : environment;

        environment = _environment;

        Target = target;
        EnvironmentName = environment?.Name ?? "No environment";

        // Ordered least to most demanding, and the default is the one that compares nothing - the same
        // thing Run has always done, so opening this window and pressing Run never quietly starts
        // judging against something the user did not choose.
        Oracles.Add(new RunOracleChoice(
            "Nothing", "Assertions decide. What Run has always done.", OracleKind.None, null));

        Oracles.Add(new RunOracleChoice(
            "Recorded snapshot",
            "Compare each response with what was recorded for this environment. A missing snapshot fails the run.",
            OracleKind.Snapshot,
            null));

        foreach (var other in allEnvironments.Where(e => e.Id != environment?.Id))
        {
            Oracles.Add(new RunOracleChoice(
                $"Compare with {other.Name}",
                $"Send everything twice - to {EnvironmentName} and to {other.Name} - and report where the two answers differ.",
                OracleKind.Environment,
                other));
        }

        // A batch states what should judge it, so the window opens on that rather than on "nothing" -
        // and it is still a picker, because the first thing anyone does when a snapshot run starts
        // failing is run it once with no oracle to see what it actually returns.
        SelectedOracle = FromBatch(batch) ?? Oracles[0];
        RebuildRows();
    }

    /// <summary>The picker row a batch's own oracle corresponds to, or null when it names none this
    /// workspace can offer - a batch comparing with an environment that has since been deleted.</summary>
    private RunOracleChoice? FromBatch(Batch? batch) => batch?.Oracle?.Kind switch
    {
        BatchOracleKind.Snapshot => Oracles.FirstOrDefault(o => o.Kind == OracleKind.Snapshot),

        BatchOracleKind.Environment => Oracles.FirstOrDefault(
            o => o.Kind == OracleKind.Environment
                 && (batch.Oracle.Environment is null
                     || string.Equals(o.Other?.Name, batch.Oracle.Environment, StringComparison.OrdinalIgnoreCase))),

        _ => null,
    };

    /// <summary>What judges each response, as one row of the picker.</summary>
    /// <param name="Other">The second environment, for <see cref="OracleKind.Environment"/>.</param>
    public sealed record RunOracleChoice(
        string Label, string Description, OracleKind Kind, WorkspaceEnvironment? Other);

    public ObservableCollection<RunOracleChoice> Oracles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OracleDescription))]
    [NotifyPropertyChangedFor(nameof(CanRecord))]
    public partial RunOracleChoice? SelectedOracle { get; set; }

    public string OracleDescription => SelectedOracle?.Description ?? "";

    /// <summary>
    /// Recording overwrites the file every later run is judged against, so it is offered where the
    /// judging is chosen rather than hidden in a menu - and it is a separate button, never something
    /// Run does when it finds nothing recorded.
    /// </summary>
    public bool CanRecord => !IsRunning;

    /// <summary>
    /// One snapshot for every environment, instead of one for the environment being run.
    /// </summary>
    /// <remarks>
    /// Off by default. Per-environment's failure mode is a redundant file; shared's is a data
    /// difference between two environments reported as a regression - and a regression tool that cries
    /// wolf stops being run.
    /// </remarks>
    [ObservableProperty]
    public partial bool ShareSnapshots { get; set; }

    /// <summary>What is being run - the folder's name, or the request's.</summary>
    public string Target { get; }

    public string EnvironmentName { get; }

    public ObservableCollection<RunStepRowViewModel> Steps { get; } = [];

    // ---- Options -----------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool StopOnFailure { get; set; }

    [ObservableProperty]
    public partial bool RecordHistory { get; set; }

    [ObservableProperty]
    public partial int DelayMilliseconds { get; set; }

    [ObservableProperty]
    public partial string? NameFilter { get; set; }

    partial void OnNameFilterChanged(string? value) => RebuildRows();

    // ---- State -------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string? Summary { get; set; }

    /// <summary>Set once a run has finished, so the summary bar can be coloured without the view
    /// re-deriving the verdict from the rows and reaching a different answer than
    /// <see cref="RunReport.Ok"/> did.</summary>
    [ObservableProperty]
    public partial bool? LastRunOk { get; set; }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanRecord));

    partial void OnLastRunOkChanged(bool? value)
    {
        OnPropertyChanged(nameof(IsVerdictOk));
        OnPropertyChanged(nameof(IsVerdictBad));
        OnPropertyChanged(nameof(HasVerdict));
    }

    /// <summary>Style-class flags for the summary bar. Derived from <see cref="LastRunOk"/> - which is
    /// <see cref="RunReport.Ok"/> verbatim - rather than re-derived in the view from the rows, so the
    /// bar cannot reach a different verdict than the report did.</summary>
    public bool IsVerdictOk => LastRunOk == true;

    public bool IsVerdictBad => LastRunOk == false;

    public bool HasVerdict => LastRunOk is not null;

    /// <summary>The report of the last completed run. Held so a caller (or a test) can ask what
    /// happened without scraping the rows.</summary>
    public RunReport? LastReport { get; private set; }

    public bool HasSteps => Steps.Count > 0;

    // ---- Commands ----------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var plan = CurrentPlan();
        if (plan.IsEmpty)
        {
            return;
        }

        var oracle = BuildOracle();
        var (byKey, progress, started) = Begin(plan);

        try
        {
            var report = await _runService.RunAsync(
                new CollectionRun(
                    plan,
                    _workspace,
                    _environment,
                    started with { CaptureResponseBodies = oracle.Kind != OracleKind.None },
                    oracle,
                    _batch?.Overlay),
                progress,
                _cancellation!.Token);

            LastReport = report;
            LastRunOk = report.Ok;
            Summary = report.Summary();
            Status = report.WasCancelled ? "Cancelled." : "Finished.";

            // Apply the final report over the rows as well as the progress updates. A run that stopped
            // early or was cancelled leaves rows the progress stream never mentioned, and they have to
            // end up saying "skipped" rather than staying on "pending" forever.
            foreach (var step in report.Steps)
            {
                if (byKey.TryGetValue(Key(step.Step), out var row))
                {
                    row.Apply(step);
                }
            }
        }
        catch (Exception ex)
        {
            // The service catches per-step failures itself, so reaching here means something outside a
            // step broke - reading the workspace's auth profiles, most likely. Report it rather than
            // letting an unobserved exception take the window down.
            LastRunOk = false;
            Summary = $"The run could not start: {ex.Message}";
            Status = "Failed to start.";
        }
        finally
        {
            Finish();
        }
    }

    /// <summary>
    /// Records what every step returns, over whatever was recorded before.
    /// </summary>
    /// <remarks>
    /// Its own button, never something Run does when it finds nothing recorded. A snapshot that writes
    /// itself on the first failing run tests nothing ever again, and does it silently.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RecordSnapshotsAsync()
    {
        var plan = CurrentPlan();
        if (plan.IsEmpty)
        {
            return;
        }

        var (_, progress, started) = Begin(plan);

        try
        {
            var report = await _recording.RecordAsync(
                new SnapshotRecording(
                    plan,
                    _workspace,
                    _environment,
                    ShareSnapshots ? SnapshotScope.Shared : SnapshotScope.Environment,
                    started),
                progress,
                _cancellation!.Token);

            LastReport = report.Run;
            LastRunOk = report.Run.Errored == 0 && report.Warnings.Count == 0;

            var scope = ShareSnapshots ? "shared across environments" : $"for {EnvironmentName}";
            Summary = $"Recorded {report.Written.Count} snapshot(s) {scope}."
                      + (report.Warnings.Count > 0 ? " " + string.Join(" ", report.Warnings) : "");

            Status = "Recorded.";
        }
        catch (Exception ex)
        {
            LastRunOk = false;
            Summary = $"The snapshots could not be recorded: {ex.Message}";
            Status = "Failed to record.";
        }
        finally
        {
            Finish();
        }
    }

    /// <summary>Everything both buttons do before they start: reset the rows, wire the progress, and
    /// put the window into its running state.</summary>
    private (Dictionary<string, RunStepRowViewModel> ByKey, IProgress<RunProgress> Progress, RunOptions Options)
        Begin(RunPlan plan)
    {
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        IsRunning = true;
        LastRunOk = null;
        Summary = null;
        Status = $"Running 0 of {plan.Count}…";
        foreach (var row in Steps)
        {
            row.Reset();
        }

        NotifyCommands();

        // Keyed on endpoint AND case: several cases share one endpoint.json, so keying on the file
        // alone throws on the duplicate and would otherwise update the wrong row.
        var byKey = Steps.ToDictionary(s => Key(s.Step), StringComparer.OrdinalIgnoreCase);
        var completed = 0;

        // Progress<T> posts back to the captured (UI) context, which is what makes it safe to touch the
        // rows from here while the run itself is on a worker.
        var progress = new Progress<RunProgress>(update =>
        {
            if (!byKey.TryGetValue(Key(update.Step), out var row))
            {
                return;
            }

            if (update.IsStarting)
            {
                row.Starting();
                Status = $"Running {completed + 1} of {update.Total} — {update.Step.QualifiedName}";
                return;
            }

            row.Apply(update.Report!);
            completed++;
            Status = $"Ran {completed} of {update.Total}";
        });

        return (byKey, progress, CurrentOptions());
    }

    private void Finish()
    {
        IsRunning = false;
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RunCommand.NotifyCanExecuteChanged();
        RecordSnapshotsCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRecord));
    }

    private static string Key(RunStep step) => $"{step.FilePath}#{step.CaseName}";

    private IOracle BuildOracle() => SelectedOracle switch
    {
        { Kind: OracleKind.Snapshot } => new SnapshotOracle(_snapshots),
        { Kind: OracleKind.Environment, Other: { } other } => new EnvironmentOracle(_runService, other),
        _ => NoOracle.Instance,
    };

    /// <summary>
    /// Opens the two bodies this step was judged from, side by side.
    /// </summary>
    /// <remarks>
    /// <para>"2 differences from Staging.json" is where the question starts, not where it ends. The
    /// same pane the environment comparison uses - left is what it was compared against, right is what
    /// came back - so the difference between the two features really is one label.</para>
    /// <para>It carries the settings hierarchy too, so <em>Ignore this field</em> writes the rule at
    /// the level you choose and into the same file the request editor would write it to.</para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanShowDifferences))]
    private async Task ShowDifferencesAsync(RunStepRowViewModel? row)
    {
        if (row?.Report is not { ResponseBody: { } response, ComparedBody: { } compared } report)
        {
            return;
        }

        try
        {
            var settings = await _settingsContext.BuildAsync(_workspace, row.Step.FilePath);

            // Accepting is offered only against a SNAPSHOT. The other side of an environment
            // comparison is a live system, and there is nothing there to write into.
            var accept = SelectedOracle is { Kind: OracleKind.Snapshot }
                ? new Services.SnapshotAcceptContext(
                    System.IO.Path.GetFileName(report.ComparedAgainst) ?? "the snapshot",
                    path => AcceptAsync(row, path))
                : null;

            await _diffPreview.ShowAsync(
                compared,
                response,
                report.ComparedAgainst ?? "expected",
                $"{row.Name} · {EnvironmentName}",
                $"{row.Name} — {report.DifferenceCount} difference{(report.DifferenceCount == 1 ? "" : "s")}",
                settings,
                accept);
        }
        catch (Exception ex)
        {
            Status = $"Could not open the comparison: {ex.Message}";
        }
    }

    private bool CanShowDifferences(RunStepRowViewModel? row) => row?.CanShowDifferences == true;

    /// <summary>
    /// Writes one field of this response into its snapshot, or the whole response when
    /// <paramref name="path"/> is null, and returns the snapshot as it now reads.
    /// </summary>
    /// <remarks>
    /// <para>The RESPONSE it writes is the one this run compared - already redacted and normalised by
    /// the same rules the recorder uses - so accepting cannot put a token in a committed file, and
    /// cannot bake in a timestamp that a normalise rule was written to remove.</para>
    /// <para>The snapshot's SCOPE is preserved, never re-decided. Accepting into a shared snapshot is
    /// a change to what every environment compares against; silently splitting it per environment
    /// here would be a different change from the one that was asked for.</para>
    /// </remarks>
    private async Task<string?> AcceptAsync(RunStepRowViewModel row, string? path)
    {
        if (row.Report is not { ResponseBody: { } response } report)
        {
            return null;
        }

        try
        {
            var lookup = await _snapshots
                .FindAsync(_workspace.RootPath, row.Step.SubjectPath, _environment?.Name);

            if (lookup.Snapshot is not { } existing)
            {
                Status = "There is no snapshot to accept into - record one first.";
                return null;
            }

            var body = response;

            if (path is { Length: > 0 })
            {
                var snapshotBody = System.Text.Json.Nodes.JsonNode.Parse(existing.BodyForComparison());
                var responseBody = System.Text.Json.Nodes.JsonNode.Parse(response);

                if (snapshotBody is null || !SnapshotAccept.Field(snapshotBody, responseBody, path))
                {
                    Status = $"Could not accept {path}.";
                    return null;
                }

                body = snapshotBody.ToJsonString(SnapshotJson.Options);
            }

            // Back through the recorder with an EMPTY policy: both sides were already redacted and
            // normalised, and what is wanted here is the stable, key-sorted serialisation so the file
            // diffs only where it changed.
            var written = SnapshotRecorder.Record(
                body,
                report.StatusCode ?? existing.Status,
                existing.Headers,
                existing.Environment,
                ResolvedSnapshotPolicy.Empty,
                existing.Case,
                recordedBy: "fubar");

            await _snapshots.SaveAsync(_workspace.RootPath, row.Step.SubjectPath, written.Snapshot);

            Status = path is { Length: > 0 }
                ? $"Accepted {path} into {System.IO.Path.GetFileName(report.ComparedAgainst)}."
                : $"Re-recorded {System.IO.Path.GetFileName(report.ComparedAgainst)}.";

            return written.Snapshot.BodyForComparison();
        }
        catch (Exception ex)
        {
            Status = $"Could not accept: {ex.Message}";
            return null;
        }
    }

    private bool CanRun() => !IsRunning && Steps.Count > 0;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        Status = "Cancelling…";
    }

    private bool CanCancel() => IsRunning;

    // ---- Plumbing ----------------------------------------------------------------------------------

    private RunPlan CurrentPlan() => _fullPlan.Filtered(NameFilter);

    private RunOptions CurrentOptions() => new()
    {
        StopOnFailure = StopOnFailure,
        RecordHistory = RecordHistory,
        DelayMilliseconds = Math.Max(0, DelayMilliseconds),
        // The filter is applied to the plan already; passing it again would filter what is left of an
        // already-filtered plan, which is the same answer only by accident.
        NameFilter = null,
    };

    private void RebuildRows()
    {
        Steps.Clear();
        foreach (var step in CurrentPlan().Steps)
        {
            Steps.Add(new RunStepRowViewModel(step));
        }

        Summary = null;
        LastRunOk = null;
        Status = Steps.Count == 0
            ? "Nothing matches."
            : $"{Steps.Count} request{(Steps.Count == 1 ? "" : "s")} ready.";

        OnPropertyChanged(nameof(HasSteps));
        RunCommand.NotifyCanExecuteChanged();
    }
}
