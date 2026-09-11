using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The Left Pane's Batches group.
///
/// <para>It reads a directory and then loads each file, which makes it the one list here that can be
/// half-built - and two of those overlapping is not hypothetical: switching workspace and re-opening
/// an editor both activate the workspace context, within a few milliseconds of each other.</para>
/// </summary>
public class BatchesSectionTests
{
    private static readonly Workspace Ws = new()
    {
        RootPath = "/w",
        Manifest = new AppManifest { Name = "w", Format = WorkspaceFormat.Endpoints },
    };

    /// <summary>Lists two batches; each load waits until the test lets it through, so two reloads can
    /// be held overlapping on purpose.</summary>
    private sealed class SlowBatchStore : IBatchStore
    {
        private readonly TaskCompletionSource _gate = new();

        public void Release() => _gate.TrySetResult();

        public IReadOnlyList<BatchSummary> ListBatches(string owner) =>
            [new BatchSummary("smoke", "/w/batches/smoke.json"),
             new BatchSummary("nightly", "/w/batches/nightly.json")];

        public async Task<Batch> LoadBatchAsync(string batchFilePath, CancellationToken ct = default)
        {
            await _gate.Task;
            return new Batch { Name = Path.GetFileNameWithoutExtension(batchFilePath) };
        }

        public Task<Batch?> FindBatchAsync(string owner, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveBatchAsync(string path, Batch batch, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public string ProposeBatchPath(string owner, string name) => $"/w/batches/{name}.json";

        public string RenameBatch(string batchFilePath, string newName) => throw new NotSupportedException();
    }

    /// <summary>
    /// The one that shipped: two overlapping reloads each cleared the list and then each added to it,
    /// so every batch appeared twice.
    /// </summary>
    [Fact]
    public async Task Two_overlapping_reloads_leave_one_copy_of_each_batch()
    {
        var store = new SlowBatchStore();
        var section = new BatchesSectionViewModel(store, new StatusLogViewModel());

        var first = section.SetWorkspaceAsync(Ws);
        var second = section.ReloadAsync();

        store.Release();
        await Task.WhenAll(first, second);

        Assert.Equal(["smoke", "nightly"], section.Rows.Select(r => r.Name));
    }

    /// <summary>A new batch is a draft - it has no file yet, so the list that reads the directory has
    /// nothing to show until the first Save.</summary>
    [Fact]
    public async Task A_new_batch_is_opened_rather_than_created()
    {
        var store = new SlowBatchStore();
        var section = new BatchesSectionViewModel(store, new StatusLogViewModel());

        store.Release();
        await section.SetWorkspaceAsync(Ws);

        string? opened = null;
        section.EditRequested += (path, _) => opened = path;

        await section.NewBatchCommand.ExecuteAsync(null);

        Assert.Equal("/w/batches/new-batch.json", opened);
    }

    /// <summary>
    /// A requests-format workspace CAN have batches, and the group says so.
    /// </summary>
    /// <remarks>
    /// This asserted the opposite for a long time, on the reading that a batch is an endpoints
    /// feature. It is not: only a CASE needs endpoints, and a step may name none.
    /// <c>TreeLookup.Matches</c> goes out of its way to resolve a request stored as
    /// <c>&lt;name&gt;.json</c> with or without the extension, <c>BatchPlanner</c> feeds a request node
    /// to <c>RunPlan.From</c> like any other, and <c>BatchEditorViewModel</c> builds its target list by
    /// flattening the whole tree. The format gate hid a working feature from half the workspaces - and
    /// would have hidden the batches themselves, so one created in a requests workspace was invisible
    /// the moment it was saved.
    /// </remarks>
    [Fact]
    public async Task A_requests_format_workspace_can_have_batches_too()
    {
        var store = new SlowBatchStore();
        var section = new BatchesSectionViewModel(store, new StatusLogViewModel());

        store.Release();
        await section.SetWorkspaceAsync(new Workspace
        {
            RootPath = "/w",
            Manifest = new AppManifest { Name = "w", Format = WorkspaceFormat.Requests },
        });

        Assert.True(section.IsAvailable);
    }

    /// <summary>With nothing open there is nothing to list, and no group to show.</summary>
    [Fact]
    public async Task No_workspace_means_no_batches()
    {
        var section = new BatchesSectionViewModel(new SlowBatchStore(), new StatusLogViewModel());

        await section.SetWorkspaceAsync(null);

        Assert.False(section.IsAvailable);
        Assert.Empty(section.Rows);
    }
}
