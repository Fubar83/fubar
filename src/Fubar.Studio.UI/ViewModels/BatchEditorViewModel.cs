using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One thing that can judge a batch's answers, as a row of the picker.</summary>
public sealed record BatchOracleChoice(string Label, BatchOracleKind Kind)
{
    public override string ToString() => Label;
}

/// <summary>
/// One step of a batch: what it calls, and which case of it.
/// </summary>
/// <remarks>
/// A step is a PATH into the collections tree, and the commonest way to break a batch is to mistype
/// one - which the file cannot tell you about and a picker cannot let you do.
/// </remarks>
public sealed partial class BatchStepRowViewModel : ViewModelBase
{
    private readonly Func<string, IReadOnlyList<string>> _casesOf;
    private readonly Func<string, bool> _isEndpoint;

    /// <summary>What the workspace actually has, as opposed to what this row can be shown holding.</summary>
    private readonly IReadOnlyList<string> _known;

    public BatchStepRowViewModel(
        BatchStep step,
        IReadOnlyList<string> targets,
        Func<string, IReadOnlyList<string>> casesOf,
        Func<string, bool> isEndpoint)
    {
        ArgumentNullException.ThrowIfNull(step);

        _casesOf = casesOf;
        _isEndpoint = isEndpoint;
        _known = targets;

        // The row's OWN list, because a step naming something the workspace no longer has still has to
        // SHOW what it names: a ComboBox whose selection is not in its items renders empty, so the one
        // step a reader must look at would be the one blank box on the screen.
        Targets = [.. targets];
        if (step.Endpoint is { Length: > 0 } && !targets.Contains(step.Endpoint, StringComparer.OrdinalIgnoreCase))
        {
            Targets.Add(step.Endpoint);
        }

        Target = step.Endpoint;
        SelectedCase = step.Case is { Length: > 0 } named ? named : EveryCase;
        RefreshCases();
    }

    /// <summary>What "this step names no case" reads as. A step with no case runs every case the
    /// endpoint has, which is a choice worth seeing rather than an empty cell.</summary>
    public const string EveryCase = "(every case)";

    /// <summary>Every endpoint and folder in the workspace, by the path a step names - plus whatever
    /// this step already named, so an unresolved one is readable rather than blank.</summary>
    public ObservableCollection<string> Targets { get; }

    public ObservableCollection<string> Cases { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnresolved))]
    [NotifyPropertyChangedFor(nameof(CanChooseCase))]
    [NotifyPropertyChangedFor(nameof(WholeTargetDescription))]
    public partial string Target { get; set; }

    [ObservableProperty]
    public partial string? SelectedCase { get; set; }

    /// <summary>True when this step names something the workspace no longer has. Shown, because a
    /// batch that quietly stopped running a step is the failure this whole feature refuses - the same
    /// reason <c>BatchPlanner</c> turns one into a step that ERRORS rather than skipping it.</summary>
    public bool IsUnresolved => !_known.Contains(Target, StringComparer.OrdinalIgnoreCase);

    /// <summary>Only an endpoint with cases has anything to choose between. A folder step runs
    /// everything under it, and a case picker beside it would suggest otherwise.</summary>
    public bool CanChooseCase => _isEndpoint(Target) && _casesOf(Target).Count > 0;

    /// <summary>What a step with nothing to choose will do, in place of the picker.</summary>
    public string WholeTargetDescription => IsUnresolved
        ? "not in this workspace"
        : _isEndpoint(Target) ? "as it stands" : "everything under it";

    public event Action? Changed;

    partial void OnTargetChanged(string value)
    {
        RefreshCases();
        Changed?.Invoke();
    }

    partial void OnSelectedCaseChanged(string? value) => Changed?.Invoke();

    private void RefreshCases()
    {
        var wanted = SelectedCase;

        Cases.Clear();
        Cases.Add(EveryCase);

        foreach (var name in _casesOf(Target))
        {
            Cases.Add(name);
        }

        // Keeps a case this endpoint no longer has rather than silently retargeting the step at
        // another one: the step is wrong, and it has to keep looking wrong.
        if (wanted is { Length: > 0 } && !Cases.Contains(wanted))
        {
            Cases.Add(wanted);
        }

        SelectedCase = wanted ?? EveryCase;
    }

    public BatchStep ToModel() => new(
        Target,
        CanChooseCase && SelectedCase is { Length: > 0 } name && name != EveryCase ? name : null);
}

/// <summary>
/// One <c>batches/&lt;name&gt;.json</c>, in the main canvas.
/// </summary>
/// <remarks>
/// <para>A batch was the last thing in this format still edited by hand, which left the two mistakes a
/// batch file invites - a mistyped endpoint path, and a name that no longer matches the file - both
/// invisible until a run went wrong.</para>
/// <para>The name is the FILE's, and saving a changed one renames the file. A selector says
/// <c>@smoke</c> and <c>IBatchStore.FindBatchAsync</c> resolves that against file names, so a batch
/// whose two names disagree is one that nothing can run by the name it displays.</para>
/// </remarks>
public sealed partial class BatchEditorViewModel : ViewModelBase, ISaveableEditor
{
    private readonly IBatchStore _batches;
    private readonly StatusLogViewModel _statusLog;
    private readonly Dictionary<string, IReadOnlyList<string>> _casesByEndpoint =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _id;
    private readonly BatchOverlay? _overlay;

    public BatchEditorViewModel(
        Batch batch,
        string filePath,
        Workspace workspace,
        IBatchStore batches,
        IReadOnlyList<string> environmentNames,
        IRequestStore requests,
        IEndpointStore endpoints,
        StatusLogViewModel statusLog)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(workspace);

        _batches = batches;
        _statusLog = statusLog;
        _id = batch.Id;

        // Carried through untouched. An overlay is the occasion's own comparison rules, which this
        // screen does not edit - and dropping what it does not show would be a silent deletion.
        _overlay = batch.Overlay;

        FilePath = filePath;
        Workspace = workspace;

        // The FILE's name, not the document's: that is what a selector resolves and what the Run
        // button asks for.
        Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        Description = batch.Description ?? "";

        var collections = System.IO.Path.Combine(workspace.RootPath, "collections");
        foreach (var node in Flatten(requests.BuildCollectionsTree(workspace.RootPath)))
        {
            var relative = System.IO.Path.GetRelativePath(collections, node.FullPath).Replace('\\', '/');
            Targets.Add(relative);

            if (node.Kind == WorkspaceNodeKind.Endpoint)
            {
                _endpoints.Add(relative);
                _casesByEndpoint[relative] = [.. endpoints.ListCases(node.FullPath).Select(c => c.Name)];
            }
        }

        foreach (var step in batch.Steps)
        {
            Steps.Add(Row(step));
        }

        foreach (var step in batch.Teardown)
        {
            Teardown.Add(Row(step));
        }

        SelectedOracle = Oracles.First(o => o.Kind == (batch.Oracle?.Kind ?? BatchOracleKind.None));

        // The empty entry is a real choice, not a placeholder: a batch that names no environment runs
        // against whichever one is active, which is what makes it usable from the toolbar AND from CI.
        EnvironmentNames = ["", .. environmentNames];
        PrimaryEnvironment = batch.Environments.FirstOrDefault() ?? "";
        OtherEnvironment = batch.Oracle?.Environment
                           ?? (batch.Environments.Count > 1 ? batch.Environments[1] : "");

        StopOnFailure = batch.Options?.StopOnFailure ?? false;
        DelayMilliseconds = batch.Options?.DelayMs ?? 0;

        Steps.CollectionChanged += (_, _) => ListChanged();
        Teardown.CollectionChanged += (_, _) => ListChanged();

        IsDirty = false;
    }

    /// <summary>Endpoints and folders, in tree order. Both are things a step may name - a folder step
    /// runs everything under it, which is what <c>RunPlan.From</c> does with one.</summary>
    private static IEnumerable<WorkspaceTreeNode> Flatten(IEnumerable<WorkspaceTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Kind == WorkspaceNodeKind.Endpoint)
            {
                yield return node;
                continue;
            }

            if (node.Kind != WorkspaceNodeKind.Folder)
            {
                continue;
            }

            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private BatchStepRowViewModel Row(BatchStep step)
    {
        var row = new BatchStepRowViewModel(
            step,
            Targets,
            target => _casesByEndpoint.TryGetValue(target, out var cases) ? cases : [],
            _endpoints.Contains);

        row.Changed += MarkDirty;
        return row;
    }

    /// <summary>Where the file is now. Not read-only: renaming the batch moves it.</summary>
    public string FilePath { get; private set; }

    public Workspace Workspace { get; }

    public ObservableCollection<string> Targets { get; } = [];

    public ObservableCollection<BatchStepRowViewModel> Steps { get; } = [];

    /// <summary>Cleanup: run after the steps whatever happened to them, and never counted towards the
    /// verdict. Its own list rather than a flag on a step, because it is a different KIND of thing -
    /// see <see cref="Batch.Teardown"/>.</summary>
    public ObservableCollection<BatchStepRowViewModel> Teardown { get; } = [];

    /// <summary>What may judge this batch, in the words the Run window uses for the same three
    /// choices - one feature described two ways is two features to learn.</summary>
    public IReadOnlyList<BatchOracleChoice> Oracles { get; } =
    [
        new("Nothing", BatchOracleKind.None),
        new("Recorded snapshots", BatchOracleKind.Snapshot),
        new("Another environment", BatchOracleKind.Environment),
    ];

    public IReadOnlyList<string> EnvironmentNames { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Oracle))]
    [NotifyPropertyChangedFor(nameof(NeedsOtherEnvironment))]
    [NotifyPropertyChangedFor(nameof(OracleDescription))]
    public partial BatchOracleChoice SelectedOracle { get; set; }

    public BatchOracleKind Oracle => SelectedOracle.Kind;

    /// <summary>The environment this batch is about. Empty means "whichever is active when it runs",
    /// which is what makes one batch usable from the toolbar AND from CI.</summary>
    [ObservableProperty]
    public partial string? PrimaryEnvironment { get; set; }

    /// <summary>The second environment, for a comparison. Only asked for when it is used.</summary>
    [ObservableProperty]
    public partial string? OtherEnvironment { get; set; }

    public bool NeedsOtherEnvironment => Oracle == BatchOracleKind.Environment;

    public string OracleDescription => Oracle switch
    {
        BatchOracleKind.Snapshot =>
            "Compare every answer with the one recorded for it. A step with no snapshot fails.",
        BatchOracleKind.Environment =>
            "Send everything to both environments and report where their answers differ.",
        _ => "The assertions decide. Nothing is compared.",
    };

    [ObservableProperty]
    public partial bool StopOnFailure { get; set; }

    [ObservableProperty]
    public partial int DelayMilliseconds { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    public bool HasSteps => Steps.Count > 0;

    public bool HasTeardown => Teardown.Count > 0;

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnDescriptionChanged(string value) => MarkDirty();

    partial void OnSelectedOracleChanged(BatchOracleChoice value) => MarkDirty();

    partial void OnPrimaryEnvironmentChanged(string? value) => MarkDirty();

    partial void OnOtherEnvironmentChanged(string? value) => MarkDirty();

    partial void OnStopOnFailureChanged(bool value) => MarkDirty();

    partial void OnDelayMillisecondsChanged(int value) => MarkDirty();

    private void MarkDirty() => IsDirty = true;

    /// <summary>Raised after a successful save, so the Left Pane's list can catch up with a rename.</summary>
    public event Action? Saved;

    // ---- The two lists ---------------------------------------------------------------------------

    [RelayCommand]
    private void AddStep() => Steps.Add(Row(new BatchStep(Targets.FirstOrDefault() ?? "")));

    [RelayCommand]
    private void AddTeardown() => Teardown.Add(Row(new BatchStep(Targets.FirstOrDefault() ?? "")));

    [RelayCommand]
    private void Remove(BatchStepRowViewModel? row)
    {
        if (row is not null && !Steps.Remove(row))
        {
            Teardown.Remove(row);
        }
    }

    /// <summary>The order is the batch's own and is never sorted - a batch that starts with a login is
    /// stating a dependency - so moving a step is the only way to change it.</summary>
    [RelayCommand]
    private void MoveUp(BatchStepRowViewModel? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(BatchStepRowViewModel? row) => Move(row, +1);

    private void Move(BatchStepRowViewModel? row, int by)
    {
        if (row is null)
        {
            return;
        }

        var list = Steps.Contains(row) ? Steps : Teardown;
        var from = list.IndexOf(row);
        var to = from + by;

        if (from >= 0 && to >= 0 && to < list.Count)
        {
            list.Move(from, to);
        }
    }

    private void ListChanged()
    {
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(HasTeardown));
        MarkDirty();
    }

    // ---- Saving ----------------------------------------------------------------------------------

    [RelayCommand]
    private async Task SaveAsync()
    {
        var name = Name.Trim();

        if (!IBatchStore.IsValidBatchName(name))
        {
            _statusLog.LogError(
                $"\"{Name}\" cannot be a batch name: the name IS the file's name, so it cannot be "
                + "empty or contain a path separator or any of \\ / : * ? \" < > |.");
            return;
        }

        try
        {
            // Written first, renamed second: a rename that fails leaves the batch where it was with
            // its new contents, rather than a saved document nobody can find.
            await _batches.SaveBatchAsync(FilePath, ToModel());
            IsDirty = false;

            if (!string.Equals(name, CurrentName, StringComparison.OrdinalIgnoreCase))
            {
                FilePath = _batches.RenameBatch(FilePath, name);
                _statusLog.Log($"Saved, and renamed to \"{name}\" - @{name} runs it now.");
            }
            else
            {
                _statusLog.Log($"Saved batch \"{name}\".");
            }

            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            // A failed RENAME still saved the contents, so the name goes back to the file's rather
            // than leaving a box on screen claiming something the disk does not say.
            Name = CurrentName;
            _statusLog.LogError($"Could not save \"{name}\": {ex.Message}");
        }
    }

    private string CurrentName => System.IO.Path.GetFileNameWithoutExtension(FilePath);

    Task ISaveableEditor.SaveAsync() => SaveCommand.ExecuteAsync(null);

    public Batch ToModel()
    {
        var environments = new List<string>();
        if (PrimaryEnvironment is { Length: > 0 } primary)
        {
            environments.Add(primary);
        }

        // The second environment is written to BOTH places it is read from, so a batch saved here runs
        // the same way from the window and from the command line.
        if (Oracle == BatchOracleKind.Environment && OtherEnvironment is { Length: > 0 } other)
        {
            environments.Add(other);
        }

        return new Batch
        {
            Id = _id,
            Name = Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Steps = [.. Steps.Select(s => s.ToModel())],
            Teardown = [.. Teardown.Select(s => s.ToModel())],
            Oracle = Oracle == BatchOracleKind.None
                ? null
                : new BatchOracle(
                    Oracle,
                    Oracle == BatchOracleKind.Environment && OtherEnvironment is { Length: > 0 } second
                        ? second
                        : null),
            Environments = environments,
            Options = StopOnFailure || DelayMilliseconds > 0
                ? new BatchOptions { StopOnFailure = StopOnFailure, DelayMs = Math.Max(0, DelayMilliseconds) }
                : null,
            Overlay = _overlay,
        };
    }
}
