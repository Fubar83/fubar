using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>
/// The <c>batches/</c> directory, and what renaming one means.
///
/// <para>A batch's name IS its file name - <c>FindBatchAsync</c> resolves <c>@smoke</c> against the
/// directory listing rather than against anything inside the files - so renaming one moves a file.
/// That makes the two failures worth pinning: a rename onto an existing batch would replace one
/// occasion with another, and a name carrying a path separator would write outside the directory.</para>
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

    [Fact]
    public async Task A_rename_moves_the_file_and_the_batch_is_found_by_its_new_name()
    {
        var path = _store.CreateBatch(_root, "smoke");

        var moved = _store.RenameBatch(path, "nightly");

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(moved));
        Assert.EndsWith("nightly.json", moved);
        Assert.NotNull(await _store.FindBatchAsync(_root, "nightly"));
        Assert.Null(await _store.FindBatchAsync(_root, "smoke"));
    }

    [Fact]
    public void Renaming_onto_an_existing_batch_is_refused()
    {
        var smoke = _store.CreateBatch(_root, "smoke");
        _store.CreateBatch(_root, "nightly");

        var failure = Assert.Throws<IOException>(() => _store.RenameBatch(smoke, "nightly"));

        Assert.Contains("nightly", failure.Message);
        Assert.True(File.Exists(smoke));
    }

    /// <summary>Both separators, on every platform: <c>Path.GetInvalidFileNameChars</c> reports only
    /// NUL and <c>/</c> on Unix, so a backslash would otherwise pass there and write one file on
    /// Windows and another on Linux out of the same workspace.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../escaped")]
    [InlineData("nested/name")]
    [InlineData("nested\\name")]
    public void A_name_that_is_not_a_file_name_is_refused(string name)
    {
        var path = _store.CreateBatch(_root, "smoke");

        Assert.Throws<ArgumentException>(() => _store.RenameBatch(path, name));
        Assert.True(File.Exists(path));
    }

    /// <summary>Case-only, which on Windows moves a file onto "itself". Done rather than skipped: the
    /// listing is what a reader sees, and a batch that will not take its own capitalisation looks
    /// broken.</summary>
    [Fact]
    public void A_case_only_rename_is_still_done()
    {
        var path = _store.CreateBatch(_root, "smoke");

        var moved = _store.RenameBatch(path, "Smoke");

        Assert.EndsWith("Smoke.json", moved);
        Assert.Equal("Smoke", _store.ListBatches(_root).Single().Name);
    }

    [Fact]
    public async Task Saving_writes_what_was_given()
    {
        var path = _store.CreateBatch(_root, "smoke");

        await _store.SaveBatchAsync(path, new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/get-order", "default")],
            Teardown = [new BatchStep("orders/delete-order")],
            Oracle = new BatchOracle(BatchOracleKind.Snapshot),
        });

        var read = await _store.LoadBatchAsync(path);

        Assert.Equal(("orders/get-order", "default"), (read.Steps.Single().Endpoint, read.Steps.Single().Case));
        Assert.Equal("orders/delete-order", read.Teardown.Single().Endpoint);
        Assert.Equal(BatchOracleKind.Snapshot, read.Oracle?.Kind);
    }
}
