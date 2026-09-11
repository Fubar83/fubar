using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>
/// A batch is a DIRECTORY and each item in it is a file.
///
/// <para>Its name IS its directory name - <c>FindBatchAsync</c> resolves <c>@smoke</c> against the
/// listing rather than against anything inside the files - so renaming one moves a directory. That
/// makes two failures worth pinning: a rename onto an existing batch would merge one occasion into
/// another, and a name carrying a path separator would write outside the workspace.</para>
/// </summary>
public class BatchStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fubar-batches-" + Guid.NewGuid().ToString("n"));
    private readonly FileBatchStore _store = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A batch already on disk under <paramref name="owner"/>, with the items named.</summary>
    private static string ExistingBatch(string owner, string name, params string[] items)
    {
        var path = Path.Combine(owner, IBatchStore.BatchesDirName, name);
        Directory.CreateDirectory(path);

        foreach (var item in items)
        {
            File.WriteAllText(Path.Combine(path, item + ".json"), "{}");
        }

        return path;
    }

    // ---- Listing -----------------------------------------------------------------------------

    [Fact]
    public void A_batch_is_a_directory_under_batches()
    {
        ExistingBatch(_root, "smoke");

        var batch = Assert.Single(_store.ListBatches(_root));
        Assert.Equal("smoke", batch.Name);
        Assert.True(Directory.Exists(batch.DirectoryPath));
    }

    /// <summary>The file name IS the order, so <c>request-2</c> has to come before <c>request-10</c> -
    /// see <c>NaturalOrder</c>. Ordinary string ordering would run them 1, 10, 2.</summary>
    [Fact]
    public void Items_come_back_in_natural_order()
    {
        var batch = ExistingBatch(_root, "smoke", "request-10", "request-2", "request-1");

        Assert.Equal(
            ["request-1", "request-2", "request-10"],
            _store.ListItems(batch).Select(i => i.Name));
    }

    /// <summary>The batch's own settings are not one of its calls.</summary>
    [Fact]
    public void The_settings_file_is_not_an_item()
    {
        var batch = ExistingBatch(_root, "smoke", "request-1");
        File.WriteAllText(Path.Combine(batch, IBatchStore.BatchFileName), "{}");

        Assert.Equal(["request-1"], _store.ListItems(batch).Select(i => i.Name));
    }

    // ---- Settings ----------------------------------------------------------------------------

    /// <summary>A batch with no <c>_batch.json</c> is a perfectly good batch that has not been told
    /// anything yet - a folder of items and no opinion about what should judge them.</summary>
    [Fact]
    public async Task A_batch_with_no_settings_file_still_loads()
    {
        var batch = ExistingBatch(_root, "smoke", "request-1");

        var loaded = await _store.LoadBatchAsync(batch);

        Assert.Equal("smoke", loaded.Name);
        Assert.Null(loaded.Oracle);
    }

    [Fact]
    public async Task Saving_writes_the_settings_beside_the_items()
    {
        var batch = ExistingBatch(_root, "smoke", "request-1");

        await _store.SaveBatchAsync(batch, new Batch
        {
            Name = "smoke",
            Oracle = new BatchOracle(BatchOracleKind.Snapshot),
        });

        Assert.True(File.Exists(Path.Combine(batch, IBatchStore.BatchFileName)));
        Assert.Equal(["request-1"], _store.ListItems(batch).Select(i => i.Name));

        var loaded = await _store.LoadBatchAsync(batch);
        Assert.Equal(BatchOracleKind.Snapshot, loaded.Oracle!.Kind);
    }

    /// <summary>The DIRECTORY's name is what a selector resolves, so a stale one inside the file never
    /// gets to disagree with it.</summary>
    [Fact]
    public async Task The_directory_name_wins_over_the_one_in_the_file()
    {
        var batch = ExistingBatch(_root, "smoke");
        File.WriteAllText(
            Path.Combine(batch, IBatchStore.BatchFileName), """{"name":"something-else"}""");

        Assert.Equal("smoke", (await _store.LoadBatchAsync(batch)).Name);
    }

    // ---- Renaming ----------------------------------------------------------------------------

    [Fact]
    public async Task A_rename_moves_the_directory_and_the_batch_is_found_by_its_new_name()
    {
        var path = ExistingBatch(_root, "smoke", "request-1");

        var moved = _store.RenameBatch(path, "nightly");

        Assert.False(Directory.Exists(path));
        Assert.True(Directory.Exists(moved));
        Assert.NotNull(await _store.FindBatchAsync(_root, "nightly"));
        Assert.Null(await _store.FindBatchAsync(_root, "smoke"));

        // The items came with it - they are IN the batch, which is the point of it being a folder.
        Assert.Equal(["request-1"], _store.ListItems(moved).Select(i => i.Name));
    }

    /// <summary>Quietly merging one occasion into another is not a rename.</summary>
    [Fact]
    public void Renaming_onto_an_existing_batch_is_refused()
    {
        var smoke = ExistingBatch(_root, "smoke");
        ExistingBatch(_root, "nightly");

        Assert.Throws<IOException>(() => _store.RenameBatch(smoke, "nightly"));
        Assert.True(Directory.Exists(smoke));
    }

    /// <summary>A move onto "the same" directory on Windows and a different one elsewhere, so it is
    /// compared ordinally and still done.</summary>
    [Fact]
    public void A_case_only_rename_is_still_done()
    {
        var path = ExistingBatch(_root, "smoke");

        var moved = _store.RenameBatch(path, "Smoke");

        Assert.Equal("Smoke", Path.GetFileName(moved));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("with/separator")]
    [InlineData("")]
    public void A_name_that_could_leave_the_directory_is_refused(string name)
    {
        var path = ExistingBatch(_root, "smoke");

        Assert.Throws<ArgumentException>(() => _store.RenameBatch(path, name));
    }

    // ---- Proposing ---------------------------------------------------------------------------

    /// <summary>A new batch lives in its editor until the first Save, so making one and changing your
    /// mind leaves nothing behind.</summary>
    [Fact]
    public void Proposing_a_batch_writes_nothing()
    {
        var proposed = _store.ProposeBatchPath(_root, "new-batch");

        Assert.False(Directory.Exists(proposed));
        Assert.Empty(_store.ListBatches(_root));
    }

    [Fact]
    public void Proposing_twice_over_an_existing_batch_gives_a_free_name()
    {
        ExistingBatch(_root, "new-batch");

        Assert.NotEqual(
            Path.Combine(_root, IBatchStore.BatchesDirName, "new-batch"),
            _store.ProposeBatchPath(_root, "new-batch"));
    }

    [Fact]
    public void Proposing_an_item_gives_a_free_file_name()
    {
        var batch = ExistingBatch(_root, "smoke", "request-1");

        Assert.Equal(
            Path.Combine(batch, "request-2.json"),
            _store.ProposeItemPath(batch, "request-2"));

        Assert.NotEqual(
            Path.Combine(batch, "request-1.json"),
            _store.ProposeItemPath(batch, "request-1"));
    }

    // ---- Finding -----------------------------------------------------------------------------

    /// <summary>A name from a selector is user input, and joining it onto a directory would let
    /// <c>@../../etc/passwd</c> address something outside the workspace.</summary>
    [Fact]
    public async Task A_name_that_is_not_there_is_null_rather_than_a_path()
    {
        ExistingBatch(_root, "smoke");

        Assert.Null(await _store.FindBatchAsync(_root, "../../etc/passwd"));
        Assert.Null(_store.FindBatchDirectory(_root, "nightly"));
    }

    /// <summary>A name is unique only within one home: the workspace root and an endpoint each have
    /// their own <c>batches/</c>, and a bare name never searches the other.</summary>
    [Fact]
    public async Task A_name_is_unique_only_within_one_home()
    {
        var endpoint = Path.Combine(_root, "collections", "orders");
        ExistingBatch(_root, "smoke");
        ExistingBatch(endpoint, "smoke");

        Assert.NotNull(await _store.FindBatchAsync(_root, "smoke"));
        Assert.NotNull(await _store.FindBatchAsync(endpoint, "smoke"));
        Assert.NotEqual(
            _store.FindBatchDirectory(_root, "smoke"),
            _store.FindBatchDirectory(endpoint, "smoke"));
    }
}
