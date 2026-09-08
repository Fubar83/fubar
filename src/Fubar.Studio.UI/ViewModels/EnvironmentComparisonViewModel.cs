using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Comparison;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Runs one collection against two environments and shows, request by request, where their answers
/// disagree.
///
/// <para><b>The rules are the point, not the run.</b> Two environments always differ on timestamps, ids
/// and anything else that moves, so a first run reports differences on nearly every row and means very
/// little. What makes the answer worth having is narrowing it - ignoring a path, matching an array by
/// key instead of position - and those rules persist on the request, so they are written once and
/// belong to whoever clones the repository next. That is why this window is not a report: selecting a
/// row puts the same editable comparison in front of you that the request editor has, and re-running is
/// one button away.</para>
///
/// <para>Rows can be worked on while the run is still going, which is what the interleaved run order
/// exists for - see <see cref="IEnvironmentPairRunService"/>.</para>
/// </summary>
public sealed partial class EnvironmentComparisonViewModel : ViewModelBase
{
    private readonly IEnvironmentPairRunService _pairRun;
    private readonly IFileComparisonService _comparison;
    private readonly RequestEditorServices _services;
    private readonly Workspace _workspace;
    private readonly RunPlan _plan;

    /// <summary>The per-row comparisons started while the run was going. Awaited before the summary is
    /// written: they are deliberately not awaited in the progress handler, so counting verdicts the
    /// moment the last request lands counts mostly unresolved rows.</summary>
    private readonly List<Task> _verdicts = [];
    private CancellationTokenSource? _cancellation;

    public EnvironmentComparisonViewModel(
        IEnvironmentPairRunService pairRun,
        IFileComparisonService comparison,
        RequestEditorServices services,
        RunPlan plan,
        Workspace workspace,
        IReadOnlyList<WorkspaceEnvironment> environments,
        string target)
    {
        _pairRun = pairRun;
        _comparison = comparison;
        _services = services;
        _plan = plan;
        _workspace = workspace;

        Target = target;
        Diff = new DiffPreviewViewModel(comparison);

        foreach (var environment in environments)
        {
            Environments.Add(environment);
        }

        // Two different environments by default when there are two to pick: the window is for comparing
        // them, and making someone choose both before anything can happen is a form filled in to say
        // what was already obvious.
        LeftEnvironment = Environments.FirstOrDefault();
        RightEnvironment = Environments.Skip(1).FirstOrDefault() ?? LeftEnvironment;

        foreach (var step in plan.Steps)
        {
            Rows.Add(new ComparisonRowViewModel(step));
        }
    }

    /// <summary>What is being compared - the folder's name, or the request's.</summary>
    public string Target { get; }

    public ObservableCollection<WorkspaceEnvironment> Environments { get; } = [];

    public ObservableCollection<ComparisonRowViewModel> Rows { get; } = [];

    /// <summary>The editable comparison for the selected row: the diff itself, the effective settings
    /// and where each one came from, and the controls that override them.</summary>
    public DiffPreviewViewModel Diff { get; }

    [ObservableProperty]
    public partial WorkspaceEnvironment? LeftEnvironment { get; set; }

    [ObservableProperty]
    public partial WorkspaceEnvironment? RightEnvironment { get; set; }

    partial void OnLeftEnvironmentChanged(WorkspaceEnvironment? value) => RunCommand.NotifyCanExecuteChanged();

    partial void OnRightEnvironmentChanged(WorkspaceEnvironment? value) => RunCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    public partial ComparisonRowViewModel? SelectedRow { get; set; }

    partial void OnSelectedRowChanged(ComparisonRowViewModel? oldValue, ComparisonRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        _ = ShowAsync(newValue);
    }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string? Summary { get; set; }

    /// <summary>Set when a run finishes with every comparable row the same. Null before the first run,
    /// so the bar is absent rather than green.</summary>
    [ObservableProperty]
    public partial bool? AllMatched { get; set; }

    partial void OnAllMatchedChanged(bool? value)
    {
        OnPropertyChanged(nameof(IsVerdictOk));
        OnPropertyChanged(nameof(IsVerdictBad));
        OnPropertyChanged(nameof(HasVerdict));
    }

    public bool IsVerdictOk => AllMatched == true;

    public bool IsVerdictBad => AllMatched == false;

    public bool HasVerdict => AllMatched is not null;

    public bool HasRows => Rows.Count > 0;

    /// <summary>The report of the last completed run, so a test can ask what happened without scraping
    /// rows.</summary>
    public EnvironmentPairReport? LastReport { get; private set; }

    // ---- Running ------------------------------------------------------------------------------------

    private bool CanRun() =>
        !IsRunning && !_plan.IsEmpty && LeftEnvironment is not null && RightEnvironment is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        IsRunning = true;
        AllMatched = null;
        Summary = null;
        Status = $"Running 0 of {_plan.Count}…";
        foreach (var row in Rows)
        {
            row.Reset();
        }

        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();

        _verdicts.Clear();
        var byPath = Rows.ToDictionary(r => r.Step.FilePath, StringComparer.OrdinalIgnoreCase);
        var completed = 0;

        // Progress<T> posts back to the captured (UI) context, which is what makes it safe to touch the
        // rows and to start a comparison from here while the run itself is on a worker.
        var progress = new Progress<StepPairProgress>(update =>
        {
            if (!byPath.TryGetValue(update.Step.FilePath, out var row))
            {
                return;
            }

            if (update.Pair is { } pair)
            {
                row.ApplyPair(pair);
                completed++;
                Status = $"Compared {completed} of {update.Total}";

                // Comparing is deliberately not awaited: it must not hold up the next request. The
                // task is kept so the summary can wait for it.
                _verdicts.Add(ResolveVerdictAsync(row));

                // The first completed row is selected so the diff is on screen immediately - the point
                // of interleaving is that rules can be written while the rest is still running.
                SelectedRow ??= row;
                return;
            }

            if (update.Left is { } left)
            {
                row.ApplyLeft(left);
                return;
            }

            row.Starting();
            Status = $"Running {completed + 1} of {update.Total} — {update.Step.Name}";
        });

        try
        {
            var report = await _pairRun.RunAsync(
                new EnvironmentPairRun(_plan, _workspace, LeftEnvironment, RightEnvironment, RunOptions.Default),
                progress,
                _cancellation.Token);

            LastReport = report;

            // Applied over the rows as well as the progress stream: a cancelled run leaves rows the
            // stream never mentioned, and they have to end up saying "skipped" rather than "pending".
            foreach (var pair in report.Pairs)
            {
                if (byPath.TryGetValue(pair.Step.FilePath, out var row) && row.Pair is null)
                {
                    row.ApplyPair(pair);
                }
            }

            Status = report.WasCancelled ? "Cancelled." : "Finished.";

            // Every row's comparison has to have landed before the rows can be counted. Progress<T>
            // posts asynchronously, so a last update can still be in the queue here - hence the yield
            // before the wait, and the sweep after it for anything the stream never mentioned.
            await Task.Yield();
            await Task.WhenAll(_verdicts.ToArray());

            foreach (var row in Rows.Where(r => r.Pair is not null && r.Verdict == ComparisonVerdict.Pending))
            {
                await ResolveVerdictAsync(row);
            }

            UpdateSummary(report);
        }
        catch (Exception ex)
        {
            // Per-step failures are the service's own business, so reaching here means something around
            // the run broke - reading the workspace's auth profiles, most likely.
            Status = "Could not run.";
            Summary = ex.Message;
            AllMatched = false;
        }
        finally
        {
            IsRunning = false;
            RunCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancellation?.Cancel();

    private void UpdateSummary(EnvironmentPairReport report)
    {
        var comparable = Rows.Count(r => r.Verdict is not ComparisonVerdict.NotComparable and not ComparisonVerdict.Pending);
        var differing = Rows.Count(r => r.Verdict is ComparisonVerdict.Differs or ComparisonVerdict.StatusDiffers);
        var uncomparable = Rows.Count(r => r.Verdict == ComparisonVerdict.NotComparable);

        var parts = new List<string> { $"{comparable - differing}/{comparable} the same" };
        if (differing > 0) parts.Add($"{differing} differ");
        if (uncomparable > 0) parts.Add($"{uncomparable} not comparable");

        Summary = $"{string.Join(", ", parts)} in {report.ElapsedMilliseconds:N0} ms";

        // Verdict counts only what could actually be compared. Calling a run bad because a response was
        // too large would report a limit of this tool as a difference between two environments.
        AllMatched = !report.WasCancelled && comparable > 0 && differing == 0;
    }

    // ---- Comparing ----------------------------------------------------------------------------------

    /// <summary>
    /// Runs the comparison for one row, using that request's own settings - which is what turns a wall
    /// of red into the two rows that matter.
    /// </summary>
    private async Task ResolveVerdictAsync(ComparisonRowViewModel row)
    {
        if (row.Verdict != ComparisonVerdict.Pending || row.Pair is not { } pair ||
            pair.Left.ResponseBody is not { } left || pair.Right.ResponseBody is not { } right)
        {
            return;
        }

        try
        {
            var context = await BuildSettingsContextAsync(row.Step.FilePath);
            var resolved = ComparisonSettingsResolver.Resolve(
                [.. context.InheritedLayers, .. Layer(context.RequestOverrides)]);

            var comparison = await _comparison.CompareTextAsync(
                left, right, ComparisonSettingsMapper.ToOptions(resolved), row.Name, row.Name);

            // The SAME count the pane shows when this row is opened, which means two things. Semantic
            // changes rather than hunks, because one contiguous run of changed lines can hold three
            // unrelated fields. And only the ones NOT ignored: the engine marks an ignored change
            // rather than dropping it, so counting them all would report differences the rules were
            // written to remove - a row reading "8" beside a pane reading "6".
            row.ApplyComparison(comparison.IsSemantic
                ? comparison.SemanticChanges.Count(c => !c.IsIgnored)
                : comparison.Result.Hunks.Count);
        }
        catch (Exception ex)
        {
            row.Verdict = ComparisonVerdict.NotComparable;
            row.Note = ex.Message;
        }
    }

    private static IEnumerable<ComparisonSettingsLayer> Layer(ComparisonSettings? overrides) =>
        overrides is null ? [] : [new ComparisonSettingsLayer(overrides, ComparisonScope.Request, "This request")];

    /// <summary>Loads the selected row's two responses into the diff pane, with that request's settings
    /// hierarchy behind the controls.</summary>
    private async Task ShowAsync(ComparisonRowViewModel? row)
    {
        if (row?.Pair is not { } pair)
        {
            return;
        }

        await Diff.LoadAsync(
            pair.Left.ResponseBody ?? "",
            pair.Right.ResponseBody ?? "",
            LeftEnvironment?.Name ?? "Left",
            RightEnvironment?.Name ?? "Right",
            row.Name,
            await BuildSettingsContextAsync(row.Step.FilePath));
    }

    /// <summary>
    /// The comparison-settings hierarchy for one request - global, then its folders, then its own
    /// overrides - plus how to persist a change at any level. The same shape the request editor builds,
    /// for the same reason: a rule written here is the same rule, in the same file.
    /// </summary>
    private async Task<DiffSettingsContext> BuildSettingsContextAsync(string requestPath)
    {
        var layers = new List<ComparisonSettingsLayer>();

        var app = await _services.AppSettings.LoadAsync();
        if (app.Comparison is { } global)
        {
            layers.Add(new ComparisonSettingsLayer(global, ComparisonScope.Global, "Global"));
        }

        var chain = await _services.InheritanceResolver.GetInheritanceChainAsync(_workspace.RootPath, requestPath);
        layers.AddRange(chain.ComparisonLayers);

        var request = await _services.RequestStore.LoadRequestAsync(requestPath);

        return new DiffSettingsContext(
            layers,
            request.Comparison?.Clone(),
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(requestPath))),
            (scope, settings) => SaveComparisonSettingsAsync(requestPath, scope, settings));
    }

    /// <summary>
    /// Writes one level's comparison overrides, then re-runs the affected rows' verdicts so the list
    /// agrees with the rule that was just saved. Fails soft at every level, like the editor's: losing a
    /// comparison over a failed write is the wrong trade in a window whose job is the comparison.
    /// </summary>
    private async Task SaveComparisonSettingsAsync(string requestPath, ComparisonScope scope, ComparisonSettings? settings)
    {
        try
        {
            switch (scope)
            {
                case ComparisonScope.Global:
                    var app = await _services.AppSettings.LoadAsync();
                    app.Comparison = settings;
                    await _services.AppSettings.SaveAsync(app);
                    _services.StatusLog.Log("Saved global comparison defaults");
                    break;

                case ComparisonScope.Folder when Path.GetDirectoryName(Path.GetDirectoryName(requestPath)) is { } folder:
                    var config = await _services.FolderConfigStore.LoadFolderConfigAsync(folder);
                    config.Comparison = settings;
                    await _services.FolderConfigStore.SaveFolderConfigAsync(folder, config);
                    _services.StatusLog.Log($"Saved comparison settings to folder {Path.GetFileName(folder)}");
                    break;

                case ComparisonScope.Request:
                    var persisted = await _services.RequestStore.LoadRequestAsync(requestPath);
                    persisted.Comparison = settings;
                    persisted.ResponseDiffIgnorePaths = [];
                    await _services.RequestStore.SaveRequestAsync(requestPath, persisted);
                    _services.StatusLog.Log($"Saved comparison settings to {Path.GetFileName(requestPath)}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _services.StatusLog.LogError($"Could not save comparison settings: {ex.Message}");
            return;
        }

        // A saved rule changes what "the same" means, so every row that has one is judged again. Only
        // the rows that already ran - this is a re-verdict, not a re-run.
        await RejudgeAsync();
    }

    /// <summary>Re-runs every completed row's comparison. Cheap next to re-sending the requests, and it
    /// is what makes a rule feel like it applied to the whole list rather than to the open row.</summary>
    private async Task RejudgeAsync()
    {
        foreach (var row in Rows.Where(r => r.Pair is not null))
        {
            if (row.Verdict is ComparisonVerdict.Differs or ComparisonVerdict.Same)
            {
                row.Verdict = ComparisonVerdict.Pending;
                row.DifferenceCount = null;
                await ResolveVerdictAsync(row);
            }
        }

        if (LastReport is { } report)
        {
            UpdateSummary(report);
        }
    }
}
