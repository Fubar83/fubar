using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Snapshots;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One thing that can judge a batch's answers, as a row of the picker.</summary>
public sealed record BatchOracleChoice(string Label, BatchOracleKind Kind)
{
    public override string ToString() => Label;
}

/// <summary>
/// One item of a batch: a file holding one way of calling the endpoint.
/// </summary>
/// <remarks>
/// A row rather than an editor. The item's body, assertions and captures are edited by opening it -
/// it is the same shape the case editor already edits - so this says only what the batch needs to
/// show: what it is called, whether it has a recorded answer, and where it sits in the order.
/// </remarks>
public sealed partial class BatchItemRowViewModel : ViewModelBase
{
    public BatchItemRowViewModel(string name, string filePath, IReadOnlyList<string> snapshotScopes)
    {
        Name = name;
        FilePath = filePath;
        SnapshotScopes = snapshotScopes;
    }

    public string Name { get; }

    public string FilePath { get; }

    /// <summary>Which environments this item has a recorded answer for. Empty means none.</summary>
    public IReadOnlyList<string> SnapshotScopes { get; }

    public bool HasSnapshot => SnapshotScopes.Count > 0;

    /// <summary>
    /// What the row says about its recorded answer.
    /// </summary>
    /// <remarks>
    /// On the row because it decides whether a judging mode is even available: "compare against
    /// recorded snapshots" means nothing for items that have none, which was previously only
    /// discoverable by running and reading the failures.
    /// </remarks>
    public string SnapshotText => SnapshotScopes.Count switch
    {
        0 => "no snapshot",
        1 => $"snapshot: {SnapshotScopes[0]}",
        _ => $"snapshots: {string.Join(", ", SnapshotScopes)}",
    };
}

/// <summary>
/// One batch: a directory of items, and what should judge them.
/// </summary>
/// <remarks>
/// <para><b>The items are FILES, and the editor does not own them.</b> It lists what the directory
/// holds and opens one on request; adding, renaming and deleting are file operations. There is no list
/// of steps to keep in step with the directory, which is what the previous shape got wrong: a batch
/// named things elsewhere in the tree, so renaming an endpoint silently broke batches that pointed at
/// it.</para>
/// <para>The ORDER is the file names', naturally sorted - <c>request-2</c> before <c>request-10</c> -
/// so reordering a batch is renaming a file and there is no index anywhere to disagree with what is on
/// disk.</para>
/// <para>The name is the DIRECTORY's, and saving a changed one moves the directory. A selector says
/// <c>@smoke</c> and <c>IBatchStore.FindBatchAsync</c> resolves that against the listing, so a batch
/// whose two names disagree is one that nothing can run by the name it displays.</para>
/// </remarks>
public sealed partial class BatchEditorViewModel : ViewModelBase, ISaveableEditor
{
    private readonly IBatchStore _batches;
    private readonly ISnapshotStore? _snapshots;
    private readonly StatusLogViewModel _statusLog;
    private readonly string _id;

    public BatchEditorViewModel(
        Batch batch,
        string directoryPath,
        Workspace workspace,
        IBatchStore batches,
        IReadOnlyList<string> environmentNames,
        StatusLogViewModel statusLog,
        ISnapshotStore? snapshots = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(workspace);

        _batches = batches;
        _statusLog = statusLog;
        _snapshots = snapshots;
        _id = batch.Id;

        DirectoryPath = directoryPath;
        Workspace = workspace;

        Name = System.IO.Path.GetFileName(directoryPath);
        Description = batch.Description ?? "";

        SelectedOracle = Oracles.First(o => o.Kind == (batch.Oracle?.Kind ?? BatchOracleKind.None));

        // The empty entry is a real choice, not a placeholder: a batch that names no environment runs
        // against whichever one is active, which is what makes it usable from the toolbar AND from CI.
        EnvironmentNames = ["", .. environmentNames];
        PrimaryEnvironment = batch.Environments.FirstOrDefault() ?? "";
        OtherEnvironment = batch.Oracle?.Environment
                           ?? (batch.Environments.Count > 1 ? batch.Environments[1] : "");

        StopOnFailure = batch.Options?.StopOnFailure ?? false;
        DelayMilliseconds = batch.Options?.DelayMs ?? 0;

        Reload();

        IsDirty = false;
    }

    /// <summary>Where the batch is now. Not read-only: renaming it moves the directory.</summary>
    public string DirectoryPath { get; private set; }

    public Workspace Workspace { get; }

    /// <summary>The items on disk, in the order they will be sent.</summary>
    public ObservableCollection<BatchItemRowViewModel> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public string ItemSummary => $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";

    /// <summary>
    /// True when at least one item has a recorded answer, so "compare against snapshots" is offered
    /// rather than chosen and then found to mean nothing.
    /// </summary>
    public bool CanJudgeBySnapshot => Items.Any(i => i.HasSnapshot);

    /// <summary>
    /// Re-reads the directory. The items are files, so the disk is the truth - and anything that adds,
    /// renames or deletes one says so by calling this rather than by keeping a list in step.
    /// </summary>
    public void Reload()
    {
        Items.Clear();

        foreach (var item in _batches.ListItems(DirectoryPath))
        {
            Items.Add(new BatchItemRowViewModel(item.Name, item.FilePath, []));
        }

        RaiseItems();
    }

    private void RaiseItems()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ItemSummary));
        OnPropertyChanged(nameof(CanJudgeBySnapshot));
    }

    /// <summary>
    /// Fills in which items have a recorded answer. Called after construction, because it reads the
    /// disk; until it lands every row says "no snapshot", which is the safe way round - it never
    /// claims a recording that is not there.
    /// </summary>
    public async Task LoadSnapshotsAsync()
    {
        if (_snapshots is null)
        {
            return;
        }

        for (var i = 0; i < Items.Count; i++)
        {
            try
            {
                var scopes = await _snapshots
                    .ScopesAsync(Workspace.RootPath, Items[i].FilePath)
                    .ConfigureAwait(true);

                if (scopes.Count > 0)
                {
                    Items[i] = new BatchItemRowViewModel(Items[i].Name, Items[i].FilePath, scopes);
                }
            }
            catch (Exception ex)
            {
                // One unreadable snapshot directory must not take the editor down with it - the batch
                // is perfectly editable without knowing what is recorded.
                _statusLog.LogWarning($"Could not read snapshots for \"{Items[i].Name}\": {ex.Message}");
            }
        }

        RaiseItems();
    }

    // ---- Items ---------------------------------------------------------------------------------

    /// <summary>Raised when an item should be opened in the main canvas.</summary>
    public event Action<string>? ItemOpenRequested;

    [RelayCommand]
    private void OpenItem(BatchItemRowViewModel? row)
    {
        if (row is not null)
        {
            ItemOpenRequested?.Invoke(row.FilePath);
        }
    }

    /// <summary>
    /// Adds an item and opens it.
    /// </summary>
    /// <remarks>
    /// Named for its POSITION - <c>request-1</c>, <c>request-2</c> - because the file name is the
    /// order. <see cref="NaturalOrder"/> is what keeps <c>request-10</c> after <c>request-2</c>
    /// instead of between <c>request-1</c> and <c>request-2</c>.
    /// </remarks>
    [RelayCommand]
    private async Task AddItemAsync()
    {
        var path = _batches.ProposeItemPath(DirectoryPath, $"request-{Items.Count + 1}");

        try
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
            await System.IO.File.WriteAllTextAsync(path, "{}").ConfigureAwait(true);

            Reload();
            ItemOpenRequested?.Invoke(path);
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not add an item to \"{Name}\": {ex.Message}");
        }
    }

    // ---- What judges it ------------------------------------------------------------------------

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
            "Compare every answer with the one recorded for it. An item with no snapshot fails.",
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

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnDescriptionChanged(string value) => MarkDirty();

    partial void OnSelectedOracleChanged(BatchOracleChoice value) => MarkDirty();

    partial void OnPrimaryEnvironmentChanged(string? value) => MarkDirty();

    partial void OnOtherEnvironmentChanged(string? value) => MarkDirty();

    partial void OnStopOnFailureChanged(bool value) => MarkDirty();

    partial void OnDelayMillisecondsChanged(int value) => MarkDirty();

    private void MarkDirty() => IsDirty = true;

    /// <summary>Raised after a successful save, so the tree catches up.</summary>
    public event Action? Saved;

    // ---- Saving --------------------------------------------------------------------------------

    [RelayCommand]
    private async Task SaveAsync()
    {
        var name = Name.Trim();

        if (!IBatchStore.IsValidBatchName(name))
        {
            _statusLog.LogError(
                $"\"{Name}\" cannot be a batch name: the name IS the directory's name, so it cannot be "
                + "empty or contain a path separator or any of \\ / : * ? \" < > |.");
            return;
        }

        try
        {
            // Written first, renamed second: a rename that fails leaves the batch where it was with
            // its new settings, rather than a saved document nobody can find.
            await _batches.SaveBatchAsync(DirectoryPath, ToModel());
            IsDirty = false;

            if (!string.Equals(name, CurrentName, StringComparison.OrdinalIgnoreCase))
            {
                DirectoryPath = _batches.RenameBatch(DirectoryPath, name);
                Reload();
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
            // A failed RENAME still saved the settings, so the name goes back to the directory's rather
            // than leaving a box on screen claiming something the disk does not say.
            Name = CurrentName;
            _statusLog.LogError($"Could not save \"{name}\": {ex.Message}");
        }
    }

    private string CurrentName => System.IO.Path.GetFileName(DirectoryPath);

    Task ISaveableEditor.SaveAsync() => SaveCommand.ExecuteAsync(null);

    /// <summary>
    /// The batch's SETTINGS. Its items are files and are not written from here - listing a directory
    /// and then writing that list back is how the two come to disagree.
    /// </summary>
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
            Oracle = Oracle == BatchOracleKind.None
                ? null
                : new BatchOracle(
                    Oracle,
                    Oracle == BatchOracleKind.Environment && OtherEnvironment is { Length: > 0 } second
                        ? second
                        : null),
            Environments = environments,
            Options = StopOnFailure || DelayMilliseconds > 0
                ? new BatchOptions { StopOnFailure = StopOnFailure, DelayMs = DelayMilliseconds }
                : null,
        };
    }
}
