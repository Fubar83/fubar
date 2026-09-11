using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Json;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>
/// An endpoint's own batches: <c>&lt;endpoint&gt;/batches/&lt;name&gt;.json</c>.
///
/// <para>Two homes now, and a name is unique only within one: the workspace's <c>batches/</c> holds
/// the occasions that cut across the tree, an endpoint's holds the ways of running that endpoint.</para>
/// </summary>
public class EndpointBatchScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fubar-epbatch-" + Guid.NewGuid().ToString("n"));
    private readonly WorkspaceService _service = new();
    private readonly FileBatchStore _batches = new();
    private readonly FileEndpointStore _endpoints = new();

    private string Endpoint => Path.Combine(_root, "collections", "orders", "get-order");

    public EndpointBatchScanTests()
    {
        Directory.CreateDirectory(Endpoint);
        Write(Path.Combine(Endpoint, "endpoint.json"), new RequestModel { Name = "Get order" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, FubarJson.Options));
    }

    /// <summary>An ITEM of a batch - the endpoint's variants live in its batches now.</summary>
    private void WriteCase(string name, string batch = "smoke") =>
        Write(Path.Combine(Endpoint, "batches", batch, name + ".json"), new EndpointCase { Name = name });

    private WorkspaceTreeNode Scan() =>
        _service.BuildCollectionsTree(_root).Single().Children.Single();

    /// <summary>A batch already on disk under <paramref name="owner"/>. Written here rather than
    /// through the store: a fixture should not lean on a production method it is not testing, and the
    /// store no longer has one - creating is what SAVING a draft does now.</summary>
    private static string ExistingBatch(string owner, string name)
    {
        var path = Path.Combine(owner, IBatchStore.BatchesDirName, name);
        Directory.CreateDirectory(path);
        return path;
    }
    // ---- Scanning ---------------------------------------------------------------------------------

    [Fact]
    public void An_endpoints_batches_are_scanned_into_their_own_list()
    {
        ExistingBatch(Endpoint, "happy");
        ExistingBatch(Endpoint, "regression");

        var endpoint = Scan();

        Assert.Equal(["happy", "regression"], endpoint.Batches.Select(b => b.Name));
        Assert.All(endpoint.Batches, b => Assert.Equal(WorkspaceNodeKind.Batch, b.Kind));
    }

    /// <summary>A batch's items are its children, and the order is their file names', naturally
    /// sorted - request-2 before request-10.</summary>
    [Fact]
    public void A_batchs_items_are_its_children_in_natural_order()
    {
        WriteCase("request-10", "happy");
        WriteCase("request-2", "happy");
        WriteCase("request-1", "happy");

        var batch = Assert.Single(Scan().Batches);

        Assert.Equal(["request-1", "request-2", "request-10"], batch.Children.Select(c => c.Name));
        Assert.All(batch.Children, c => Assert.Equal(WorkspaceNodeKind.Case, c.Kind));
    }

    /// <summary>
    /// The one that keeps a run honest: an endpoint's Children is what running IT sends, and running
    /// an endpoint sends the call itself. Its items belong to a batch, and are sent by running that.
    /// </summary>
    [Fact]
    public void An_endpoints_own_children_are_empty()
    {
        WriteCase("request-1");
        ExistingBatch(Endpoint, "happy");

        Assert.Empty(Scan().Children);
    }

    [Fact]
    public void An_endpoint_with_no_batches_directory_has_none()
    {
        Assert.Empty(Scan().Batches);
    }

    /// <summary><c>batches/</c> is a reserved name an endpoint owns, like <c>snapshots/</c> - never a
    /// folder in the tree, and never something nested under the endpoint either.</summary>
    [Fact]
    public void The_batches_directory_is_not_a_folder_in_the_tree()
    {
        ExistingBatch(Endpoint, "happy");

        var endpoint = Scan();

        Assert.DoesNotContain(endpoint.Children, c => c.Name == "batches");
        Assert.DoesNotContain(endpoint.Nested, c => c.Name == "batches");
    }

    // ---- Two homes --------------------------------------------------------------------------------

    /// <summary>The same name in both homes is two different batches, and neither shadows the other.</summary>
    [Fact]
    public async Task A_name_is_unique_only_within_one_home()
    {
        ExistingBatch(_root, "happy");
        ExistingBatch(Endpoint, "happy");

        Assert.NotNull(await _batches.FindBatchAsync(_root, "happy"));
        Assert.NotNull(await _batches.FindBatchAsync(Endpoint, "happy"));
        Assert.Single(_batches.ListBatches(_root));
        Assert.Single(_batches.ListBatches(Endpoint));
    }

    [Fact]
    public async Task An_endpoints_batch_is_not_found_from_the_workspace()
    {
        ExistingBatch(Endpoint, "happy");

        Assert.Null(await _batches.FindBatchAsync(_root, "happy"));
    }

    /// <summary>A batch directory is two levels down inside its endpoint, and an ITEM is three - so
    /// both have to climb out to the same place.</summary>
    [Fact]
    public void A_batch_and_its_items_know_which_endpoint_they_belong_to()
    {
        var path = ExistingBatch(Endpoint, "happy");
        WriteCase("request-1", "happy");

        Assert.Equal(Endpoint, _endpoints.EndpointDirectoryOf(path));
        Assert.Equal(Endpoint, _endpoints.EndpointDirectoryOf(Path.Combine(path, "request-1.json")));
    }

    [Fact]
    public void A_workspace_batch_belongs_to_no_endpoint()
    {
        var path = ExistingBatch(_root, "smoke");

        Assert.Null(_endpoints.EndpointDirectoryOf(path));
    }
}
