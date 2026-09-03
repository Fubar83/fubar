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
    private readonly Workspace _workspace;
    private readonly WorkspaceEnvironment? _environment;
    private readonly RunPlan _fullPlan;
    private CancellationTokenSource? _cancellation;

    public CollectionRunViewModel(
        ICollectionRunService runService,
        RunPlan plan,
        Workspace workspace,
        WorkspaceEnvironment? environment,
        string target)
    {
        _runService = runService;
        _fullPlan = plan;
        _workspace = workspace;
        _environment = environment;

        Target = target;
        EnvironmentName = environment?.Name ?? "No environment";
        RebuildRows();
    }

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

        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();

        var byPath = Steps.ToDictionary(s => s.Step.FilePath, StringComparer.OrdinalIgnoreCase);
        var completed = 0;

        // Progress<T> posts back to the captured (UI) context, which is what makes it safe to touch the
        // rows from here while the run itself is on a worker.
        var progress = new Progress<RunProgress>(update =>
        {
            if (!byPath.TryGetValue(update.Step.FilePath, out var row))
            {
                return;
            }

            if (update.IsStarting)
            {
                row.Starting();
                Status = $"Running {completed + 1} of {update.Total} — {update.Step.Name}";
                return;
            }

            row.Apply(update.Report!);
            completed++;
            Status = $"Ran {completed} of {update.Total}";
        });

        try
        {
            var report = await _runService.RunAsync(
                new CollectionRun(plan, _workspace, _environment, CurrentOptions()),
                progress,
                _cancellation.Token);

            LastReport = report;
            LastRunOk = report.Ok;
            Summary = report.Summary();
            Status = report.WasCancelled ? "Cancelled." : "Finished.";

            // Apply the final report over the rows as well as the progress updates. A run that stopped
            // early or was cancelled leaves rows the progress stream never mentioned, and they have to
            // end up saying "skipped" rather than staying on "pending" forever.
            foreach (var step in report.Steps)
            {
                if (byPath.TryGetValue(step.Step.FilePath, out var row))
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
            IsRunning = false;
            RunCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
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
