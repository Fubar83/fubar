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
        Directory.CreateDirectory(Path.Combine(Endpoint, "cases"));
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

    private void WriteCase(string name) =>
        Write(Path.Combine(Endpoint, "cases", name + ".json"), new EndpointCase { Name = name });

    private WorkspaceTreeNode Scan() =>
        _service.BuildCollectionsTree(_root).Single().Children.Single();

    // ---- Scanning ---------------------------------------------------------------------------------

    [Fact]
    public void An_endpoints_batches_are_scanned_into_their_own_list()
    {
        WriteCase("default");
        _batches.CreateBatch(Endpoint, "happy");
        _batches.CreateBatch(Endpoint, "regression");

        var endpoint = Scan();

        Assert.Equal(["happy", "regression"], endpoint.Batches.Select(b => b.Name));
        Assert.All(endpoint.Batches, b => Assert.Equal(WorkspaceNodeKind.Batch, b.Kind));
    }

    /// <summary>The one that keeps a run honest: batches are never children, so a run of the endpoint
    /// still sends its cases and nothing else.</summary>
    [Fact]
    public void Batches_are_not_among_the_endpoints_children()
    {
        WriteCase("default");
        _batches.CreateBatch(Endpoint, "happy");

        var endpoint = Scan();

        Assert.Equal(["default"], endpoint.Children.Select(c => c.Name));
    }

    /// <summary>An endpoint that only has batches still has no cases - it is sent as it stands, and
    /// the batches are things you can choose to run instead.</summary>
    [Fact]
    public void An_endpoint_with_only_batches_has_no_cases()
    {
        _batches.CreateBatch(Endpoint, "happy");

        var endpoint = Scan();

        Assert.Empty(endpoint.Children);
        Assert.Single(endpoint.Batches);
    }

    [Fact]
    public void An_endpoint_with_no_batches_directory_has_none()
    {
        WriteCase("default");

        Assert.Empty(Scan().Batches);
    }

    /// <summary><c>batches/</c> is a reserved name an endpoint owns, like <c>cases/</c> and
    /// <c>snapshots/</c> - never a folder in the tree.</summary>
    [Fact]
    public void The_batches_directory_is_not_a_folder_in_the_tree()
    {
        _batches.CreateBatch(Endpoint, "happy");

        Assert.DoesNotContain(Scan().Children, c => c.Name == "batches");
    }

    // ---- Two homes --------------------------------------------------------------------------------

    /// <summary>The same name in both homes is two different batches, and neither shadows the other.</summary>
    [Fact]
    public async Task A_name_is_unique_only_within_one_home()
    {
        _batches.CreateBatch(_root, "happy");
        _batches.CreateBatch(Endpoint, "happy");

        var workspaceOwned = await _batches.FindBatchAsync(_root, "happy");
        var endpointOwned = await _batches.FindBatchAsync(Endpoint, "happy");

        Assert.NotNull(workspaceOwned);
        Assert.NotNull(endpointOwned);
        Assert.Single(_batches.ListBatches(_root));
        Assert.Single(_batches.ListBatches(Endpoint));
    }

    [Fact]
    public async Task An_endpoints_batch_is_not_found_from_the_workspace()
    {
        _batches.CreateBatch(Endpoint, "happy");

        Assert.Null(await _batches.FindBatchAsync(_root, "happy"));
    }

    /// <summary>A batch file is two levels down inside its endpoint, exactly like a case, so the
    /// endpoint it belongs to is found the same way.</summary>
    [Fact]
    public void An_endpoint_batch_knows_which_endpoint_it_belongs_to()
    {
        var path = _batches.CreateBatch(Endpoint, "happy");

        Assert.Equal(Endpoint, _endpoints.EndpointDirectoryOf(path));
    }

    [Fact]
    public void A_workspace_batch_belongs_to_no_endpoint()
    {
        var path = _batches.CreateBatch(_root, "smoke");

        Assert.Null(_endpoints.EndpointDirectoryOf(path));
    }
}
