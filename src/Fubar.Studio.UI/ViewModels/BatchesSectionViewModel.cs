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
        Name = batch.Name;
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

    /// <summary>Called whenever the active workspace changes (or closes).</summary>
    public async Task SetWorkspaceAsync(Workspace? workspace)
    {
        _workspace = workspace;
        IsAvailable = workspace?.Manifest.Format == WorkspaceFormat.Endpoints;

        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        Rows.Clear();

        if (_workspace is not { } workspace || !IsAvailable)
        {
            return;
        }

        foreach (var summary in _batches.ListBatches(workspace.RootPath))
        {
            try
            {
                Rows.Add(new BatchRowViewModel(summary, await _batches.LoadBatchAsync(summary.FilePath)));
            }
            catch (Exception ex)
            {
                // One unreadable batch does not hide the others, and it is SAID: a batch that quietly
                // vanished from the list is a batch nobody runs and nobody misses.
                _statusLog.LogWarning($"Could not read the batch \"{summary.Name}\": {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void Run(BatchRowViewModel? row)
    {
        if (row is not null)
        {
            RunRequested?.Invoke(row);
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

        var path = _batches.CreateBatch(workspace.RootPath, "new-batch");
        _statusLog.Log($"Created batch: {path}");

        await ReloadAsync();
    }
}
