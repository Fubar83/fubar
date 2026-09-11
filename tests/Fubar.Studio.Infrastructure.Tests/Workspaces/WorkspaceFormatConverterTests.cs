using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Json;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>
/// Turning a requests workspace into an endpoints one.
///
/// <para>This rewrites committed files, so the tests that matter most are the ones about what it
/// refuses to touch: a snapshot is not a request, a name collision is not something to guess at, and
/// the format field is not stamped until something actually converted.</para>
/// </summary>
public class WorkspaceFormatConverterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fubar-conv-" + Guid.NewGuid().ToString("n"));
    private readonly WorkspaceService _service = new();
    private readonly WorkspaceFormatConverter _converter;

    public WorkspaceFormatConverterTests()
    {
        _converter = new WorkspaceFormatConverter(_service);
        Directory.CreateDirectory(Collections);
    }

    private string Collections => Path.Combine(_root, "collections");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private Workspace Workspace(WorkspaceFormat format = WorkspaceFormat.Requests) => new()
    {
        RootPath = _root,
        Manifest = new AppManifest { Name = "t", Format = format },
    };

    private string WriteRequest(string relativePath, Action<RequestModel>? configure = null)
    {
        var path = Path.Combine(Collections, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var request = new RequestModel
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Method = "GET",
            Url = "{{baseUrl}}/orders",
            Headers = [new KeyValueItem { Key = "Accept", Value = "application/json" }],
            QueryParams = [new KeyValueItem { Key = "include", Value = "lines" }],
            Assertions = [new Assertion { Expected = "200" }],
        };

        configure?.Invoke(request);
        File.WriteAllText(path, JsonSerializer.Serialize(request, FubarJson.Options));
        return path;
    }

    // ---- The split -------------------------------------------------------------------------------

    /// <summary>The whole point: the operation on one side, the invocation on the other, so the
    /// second case someone adds is a change to one small file rather than a copy of everything.</summary>
    [Fact]
    public async Task A_request_becomes_an_endpoint_and_one_case()
    {
        WriteRequest("orders/Get order.json");

        var result = await _converter.ConvertAsync(Workspace());

        Assert.Equal(1, result.EndpointsCreated);
        Assert.False(File.Exists(Path.Combine(Collections, "orders", "Get order.json")));

        var endpointJson = Path.Combine(Collections, "orders", "Get order", "endpoint.json");
        var caseJson = Path.Combine(Collections, "orders", "Get order", "batches", "default", "request-1.json");
        Assert.True(File.Exists(endpointJson));
        Assert.True(File.Exists(caseJson));

        var endpoint = JsonSerializer.Deserialize<RequestModel>(File.ReadAllText(endpointJson), FubarJson.Options)!;
        var converted = JsonSerializer.Deserialize<EndpointCase>(File.ReadAllText(caseJson), FubarJson.Options)!;

        // True every time it is called.
        Assert.Equal("GET", endpoint.Method);
        Assert.Equal("{{baseUrl}}/orders", endpoint.Url);
        Assert.Single(endpoint.Headers);

        // True of this call.
        Assert.Single(converted.QueryParams);
        Assert.Single(converted.Assertions);

        // Not left on both sides, or the case would silently stop mattering the moment the endpoint
        // was edited.
        Assert.Empty(endpoint.QueryParams);
        Assert.Empty(endpoint.Assertions);
    }

    [Fact]
    public async Task The_format_field_is_stamped_so_nothing_has_to_sniff_the_files()
    {
        WriteRequest("Ping.json");
        var workspace = Workspace();

        await _converter.ConvertAsync(workspace);

        var reloaded = await _service.LoadWorkspaceAsync(_root);
        Assert.Equal(WorkspaceFormat.Endpoints, reloaded.Manifest.Format);
    }

    /// <summary>The originals are copied before anything moves, under .fubar/ - which is git-ignored,
    /// so the safety net does not itself become a thousand added files in the review.</summary>
    [Fact]
    public async Task The_originals_are_backed_up_first()
    {
        WriteRequest("Ping.json");

        var result = await _converter.ConvertAsync(Workspace());

        Assert.True(File.Exists(Path.Combine(result.BackupPath, "collections", "Ping.json")));
        Assert.StartsWith(Path.Combine(_root, ".fubar"), result.BackupPath, StringComparison.Ordinal);
    }

    // ---- What it must not touch ------------------------------------------------------------------

    /// <summary>The worst outcome this could have: a recorded snapshot turned into an endpoint that
    /// sends nothing, with the real snapshot deleted underneath it.</summary>
    [Fact]
    public async Task A_snapshot_is_not_mistaken_for_a_request()
    {
        WriteRequest("Ping.json");

        var snapshots = Path.Combine(Collections, "Ping.snapshots");
        Directory.CreateDirectory(snapshots);
        File.WriteAllText(Path.Combine(snapshots, "staging.json"), """{"status":200}""");

        var result = await _converter.ConvertAsync(Workspace());

        Assert.Equal(1, result.EndpointsCreated);
        Assert.False(Directory.Exists(Path.Combine(Collections, "Ping", "staging")));
    }

    /// <summary>Left behind, a snapshot is orphaned beside a file that no longer exists, and the first
    /// run after converting reports "no snapshot" for a workspace that had them all along.</summary>
    [Fact]
    public async Task Snapshots_move_into_the_case_they_belong_to()
    {
        WriteRequest("Ping.json");

        var snapshots = Path.Combine(Collections, "Ping.snapshots");
        Directory.CreateDirectory(snapshots);
        File.WriteAllText(Path.Combine(snapshots, "staging.json"), """{"status":200}""");

        await _converter.ConvertAsync(Workspace());

        Assert.True(File.Exists(
            Path.Combine(Collections, "Ping", "snapshots", "request-1", "staging.json")));
        Assert.False(Directory.Exists(snapshots));
    }

    [Fact]
    public void A_folder_config_is_not_a_request()
    {
        WriteRequest("orders/Get order.json");
        File.WriteAllText(Path.Combine(Collections, "orders", "_folder.json"), "{}");

        var plan = _converter.Preview(Workspace());

        Assert.Equal("Get order", Path.GetFileNameWithoutExtension(Assert.Single(plan.Steps).RequestPath));
    }

    /// <summary>Refused rather than renamed around: "Get order.json" beside a folder called "Get
    /// order" is ambiguous, and guessing produces a tree the user did not write.</summary>
    [Fact]
    public void A_name_collision_blocks_the_whole_conversion()
    {
        WriteRequest("Get order.json");
        Directory.CreateDirectory(Path.Combine(Collections, "Get order"));

        var plan = _converter.Preview(Workspace());

        Assert.False(plan.CanRun);
        Assert.Contains(plan.Blockers, b => b.Contains("Get order", StringComparison.Ordinal));
    }

    [Fact]
    public void A_workspace_already_in_the_new_format_is_refused_rather_than_converted_twice()
    {
        WriteRequest("Ping.json");

        var plan = _converter.Preview(Workspace(WorkspaceFormat.Endpoints));

        Assert.False(plan.CanRun);
    }

    /// <summary>Running it again after a half-finished conversion must be safe, so what is already an
    /// endpoint is not seen as a request the second time round.</summary>
    [Fact]
    public async Task Converting_twice_leaves_the_second_run_with_nothing_to_do()
    {
        WriteRequest("Ping.json");
        await _converter.ConvertAsync(Workspace());

        var plan = _converter.Preview(Workspace());

        Assert.Empty(plan.Steps);
    }

    // ---- The tree afterwards ---------------------------------------------------------------------

    /// <summary>An endpoint is a directory and is not a folder; its cases are its children, and
    /// snapshots/ is not one of them.</summary>
    [Fact]
    public async Task The_tree_reads_the_converted_workspace_as_endpoints_and_batches()
    {
        WriteRequest("orders/Get order.json");
        await _converter.ConvertAsync(Workspace());

        var orders = Assert.Single(_service.BuildCollectionsTree(_root));
        Assert.Equal(WorkspaceNodeKind.Folder, orders.Kind);

        var endpoint = Assert.Single(orders.Children);
        Assert.Equal(WorkspaceNodeKind.Endpoint, endpoint.Kind);
        Assert.Equal("Get order", endpoint.Name);

        // Running the ENDPOINT sends the call itself, so it has no children of its own; the converted
        // request becomes the one item of the one batch.
        Assert.Empty(endpoint.Children);

        var batch = Assert.Single(endpoint.Batches);
        Assert.Equal("default", batch.Name);

        var item = Assert.Single(batch.Children);
        Assert.Equal(WorkspaceNodeKind.Case, item.Kind);
        Assert.Equal("request-1", item.Name);
    }
}
