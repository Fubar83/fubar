using System.Text.Json;
using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Infrastructure.Json;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// What the chain says AT a folder, which is what the folder editor's Rules tab shows.
///
/// <para>Anchored on the folder's own <c>_folder.json</c> because the chain walks up from a file's
/// PARENT: naming the file inside the folder is what makes the folder itself the innermost level
/// rather than the one above it.</para>
/// </summary>
public class FolderRulesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fubar-folderrules-" + Guid.NewGuid().ToString("n"));
    private readonly WorkspaceService _service = new();

    private string Collections => Path.Combine(_root, "collections");

    private string Orders => Path.Combine(Collections, "orders");

    private Workspace Workspace => new()
    {
        RootPath = _root,
        Manifest = new AppManifest { Name = "w", Format = WorkspaceFormat.Endpoints },
    };

    public FolderRulesTests() => Directory.CreateDirectory(Orders);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private void WriteFolder(string folder, FolderConfig config) =>
        File.WriteAllText(
            Path.Combine(folder, "_folder.json"), JsonSerializer.Serialize(config, FubarJson.Options));

    private RequestComparisonSettings Sut() =>
        new(new NoAppSettings(), _service, _service, new FileEndpointStore());

    private sealed class NoAppSettings : IAppSettingsService
    {
        public AppSettings Load() => new();

        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(new AppSettings());

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>The folder's own rules are in force at it - the chain stops here rather than one
    /// level up.</summary>
    [Fact]
    public async Task A_folders_own_rules_apply_at_it()
    {
        WriteFolder(Orders, new FolderConfig
        {
            Comparison = new ComparisonSettings { IgnoredPaths = new InheritedPaths { Add = ["$.traceId"] } },
        });

        var rules = await Sut().ResolveFolderRulesAsync(Workspace, Orders, TestContext.Current.CancellationToken);

        var ignored = Assert.Single(rules.Comparison.IgnoredPaths);
        Assert.Equal("$.traceId", ignored.Path);
        Assert.Equal("Folder: orders", ignored.SourceName);
    }

    /// <summary>And an ancestor's still are, named as its own - which is what lets the editor grey
    /// them and refuse to delete them from here.</summary>
    [Fact]
    public async Task An_ancestors_rules_apply_and_keep_their_name()
    {
        WriteFolder(Collections, new FolderConfig
        {
            Comparison = new ComparisonSettings { IgnoredPaths = new InheritedPaths { Add = ["$.meta"] } },
        });

        WriteFolder(Orders, new FolderConfig
        {
            Comparison = new ComparisonSettings { IgnoredPaths = new InheritedPaths { Add = ["$.traceId"] } },
        });

        var rules = await Sut().ResolveFolderRulesAsync(Workspace, Orders, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("$.meta", "Folder: Workspace Root"), ("$.traceId", "Folder: orders")],
            rules.Comparison.IgnoredPaths.Select(p => (p.Path, p.SourceName)));
    }

    /// <summary>Nothing BELOW a folder contributes: an endpoint's own rules are not in force "at" the
    /// folder, and showing them there would invite editing them from the wrong level.</summary>
    [Fact]
    public async Task An_endpoints_own_rules_do_not_apply_at_the_folder()
    {
        var endpoint = Path.Combine(Orders, "get-order");
        Directory.CreateDirectory(endpoint);

        File.WriteAllText(
            Path.Combine(endpoint, "endpoint.json"),
            JsonSerializer.Serialize(
                new RequestModel
                {
                    Name = "Get order",
                    Comparison = new ComparisonSettings { IgnoredPaths = new InheritedPaths { Add = ["$.only-mine"] } },
                },
                FubarJson.Options));

        var rules = await Sut().ResolveFolderRulesAsync(Workspace, Orders, TestContext.Current.CancellationToken);

        Assert.Empty(rules.Comparison.IgnoredPaths);
    }

    [Fact]
    public async Task A_folder_with_no_file_of_its_own_still_resolves()
    {
        var rules = await Sut().ResolveFolderRulesAsync(Workspace, Orders, TestContext.Current.CancellationToken);

        Assert.Empty(rules.Comparison.IgnoredPaths);
        Assert.Empty(rules.Tolerances);
    }

    /// <summary>A folder holds a snapshot policy like an endpoint does, and the editor can write one.</summary>
    [Fact]
    public async Task A_folders_snapshot_policy_applies_at_it()
    {
        WriteFolder(Orders, new FolderConfig
        {
            Snapshot = new Core.Snapshots.SnapshotPolicy
            {
                Redact = new Core.Snapshots.InheritedRules
                {
                    Add = [new Core.Snapshots.SnapshotRule("$..token", "<redacted>")],
                },
            },
        });

        var rules = await Sut().ResolveFolderRulesAsync(Workspace, Orders, TestContext.Current.CancellationToken);

        Assert.Equal("$..token", Assert.Single(rules.Snapshot.Redact).Path);
    }
}
