using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One batch in the Left Pane's list.</summary>
public sealed partial class BatchRowViewModel : ViewModelBase
{
    public BatchRowViewModel(BatchSummary summary, Batch batch)
    {
        FilePath = summary.FilePath;

        // The FILE's name, not the document's. A selector says @smoke and IBatchStore.FindBatchAsync
        // resolves that against the directory listing, so showing the name INSIDE the file would put a
        // Run button next to a name nothing can be found by the moment the two disagree.
        Name = summary.Name;
        Model = batch;

        var steps = $"{batch.Steps.Count} step{(batch.Steps.Count == 1 ? "" : "s")}";

        // What it will DO, in the row, because a batch's name says what it is for and the judging is
        // what makes running it mean something.
        var judge = batch.Oracle?.Kind switch
        {
            BatchOracleKind.Snapshot => "against snapshots",
            BatchOracleKind.Environment => $"against {batch.Oracle.Environment ?? "another environment"}",
            _ => "assertions only",
        };

        var where = batch.Environments.Count > 0 ? $" · {string.Join(" vs ", batch.Environments)}" : "";

        Detail = $"{steps} · {judge}{where}";
    }

    public Batch Model { get; }

    public string FilePath { get; }

    public string Name { get; }

    public string Detail { get; }
}

/// <summary>
/// The Left Pane's "Batches" group: the occasions this workspace has a name for.
/// </summary>
/// <remarks>
/// <para>Beside the tree rather than in it, because the tree says what the API HAS and a batch says
/// what to call on a particular occasion - two lists that change for different reasons. It is the same
/// separation the Environments group already has.</para>
/// <para>Only shown in the endpoints format: batches address endpoints and cases, and offering them in
/// a workspace that has neither would be a group whose every row could only fail.</para>
/// </remarks>
public sealed partial class BatchesSectionViewModel : ViewModelBase
{
    private readonly IBatchStore _batches;
    private readonly StatusLogViewModel _statusLog;
    private Workspace? _workspace;

    public BatchesSectionViewModel(IBatchStore batches, StatusLogViewModel statusLog)
    {
        _batches = batches;
        _statusLog = statusLog;

        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRows));
    }

    public ObservableCollection<BatchRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Whether this workspace can have batches at all.</summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    /// <summary>Raised when a row is chosen to run - <c>MainViewModel</c> opens the Run window for it,
    /// because a run needs the active environment, which lives beside this view model.</summary>
    public event Action<BatchRowViewModel>? RunRequested;

    /// <summary>
    /// Raised when a batch is chosen for editing, with its file and a freshly read copy of it -
    /// <c>MainViewModel</c> opens a <see cref="BatchEditorViewModel"/> for it in the main canvas.
    /// </summary>
    /// <remarks>
    /// Re-read rather than the row's own <c>Model</c>, which was loaded when the workspace last
    /// changed: an editor opened on a stale copy would save it back over whatever has happened to the
    /// file since.
    /// </remarks>
    public event Action<string, Batch>? EditRequested;

    /// <summary>Called whenever the active workspace changes (or closes).</summary>
    public async Task SetWorkspaceAsync(Workspace? workspace)
    {
        _workspace = workspace;
        IsAvailable = workspace?.Manifest.Format == WorkspaceFormat.Endpoints;

        await ReloadAsync();
    }

    /// <summary>
    /// Rebuilds the list from the <c>batches/</c> directory.
    /// </summary>
    /// <remarks>
    /// Built into a local list and published in one step, with a generation guard, because this used
    /// to clear <see cref="Rows"/> and then refill it one <c>await</c> at a time: two reloads
    /// overlapping - which is what switching workspace and re-opening an editor does within a few
    /// milliseconds of each other - both cleared and then both added, and the group showed every batch
    /// twice. It also means the list never blinks empty on the way.
    /// </remarks>
    public async Task ReloadAsync()
    {
        var generation = ++_reloadGeneration;

        var loaded = new List<BatchRowViewModel>();

        if (_workspace is { } workspace && IsAvailable)
        {
            foreach (var summary in _batches.ListBatches(workspace.RootPath))
            {
                try
                {
                    loaded.Add(new BatchRowViewModel(summary, await _batches.LoadBatchAsync(summary.FilePath)));
                }
                catch (Exception ex)
                {
                    // One unreadable batch does not hide the others, and it is SAID: a batch that
                    // quietly vanished from the list is a batch nobody runs and nobody misses.
                    _statusLog.LogWarning($"Could not read the batch \"{summary.Name}\": {ex.Message}");
                }
            }
        }

        // A newer reload started while this one was reading, so this answer is already stale.
        if (generation != _reloadGeneration)
        {
            return;
        }

        Rows.Clear();
        foreach (var row in loaded)
        {
            Rows.Add(row);
        }
    }

    private int _reloadGeneration;

    [RelayCommand]
    private void Run(BatchRowViewModel? row)
    {
        if (row is not null)
        {
            RunRequested?.Invoke(row);
        }
    }

    [RelayCommand]
    private async Task EditAsync(BatchRowViewModel? row)
    {
        if (row is not null)
        {
            await OpenAsync(row.FilePath, row.Name);
        }
    }

    private async Task OpenAsync(string filePath, string name)
    {
        try
        {
            EditRequested?.Invoke(filePath, await _batches.LoadBatchAsync(filePath));
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not open the batch \"{name}\": {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task NewBatchAsync()
    {
        if (_workspace is not { } workspace)
        {
            _statusLog.Log("Open a workspace before creating a batch.");
            return;
        }

        // Proposed, not created: a new batch lives in its editor until the first Save, so making one
        // and changing your mind leaves no new-batch.json behind. That also means it is not in this
        // list yet - the list reads the directory, and there is nothing there to read.
        var path = _batches.ProposeBatchPath(workspace.RootPath, "new-batch");

        // Opened straight away. A new batch is empty - a row saying "0 steps" with no way in but a
        // text editor is what made batches a JSON-editing job in the first place.
        EditRequested?.Invoke(path, new Batch { Name = System.IO.Path.GetFileNameWithoutExtension(path) });

        await Task.CompletedTask;
    }
}
