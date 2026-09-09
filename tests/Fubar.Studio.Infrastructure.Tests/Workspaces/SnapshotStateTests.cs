using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Json;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>
/// What the tree says about recorded answers.
///
/// <para><c>Stale</c> is why this exists. A green regression run against a snapshot recorded BEFORE
/// the endpoint or case was last edited is a lie, and the tree is the only place anyone can notice
/// before running.</para>
/// </summary>
public class SnapshotStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fubar-snapstate-" + Guid.NewGuid().ToString("n"));
    private readonly WorkspaceService _service = new();

    private string Endpoint => Path.Combine(_root, "collections", "orders", "get-order");

    public SnapshotStateTests()
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

    /// <summary>Written with an explicit timestamp, because "before" and "after" is the whole
    /// question and a test that writes both files in the same millisecond asks nothing.</summary>
    private void WriteSnapshot(string caseName, string environment, DateTime writtenUtc)
    {
        var path = Path.Combine(Endpoint, "snapshots", caseName, environment + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"status":200}""");
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    private WorkspaceTreeNode Scan() =>
        _service.BuildCollectionsTree(_root).Single().Children.Single();

    [Fact]
    public void Nothing_recorded_reads_as_none()
    {
        WriteCase("default");

        Assert.Equal(SnapshotState.None, Scan().Children.Single().Snapshots);
        Assert.Equal(SnapshotState.None, Scan().Snapshots);
    }

    [Fact]
    public void A_snapshot_recorded_after_the_last_edit_is_current()
    {
        WriteCase("default");
        WriteSnapshot("default", "Staging", DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(SnapshotState.Recorded, Scan().Children.Single().Snapshots);
    }

    /// <summary>The one that matters.</summary>
    [Fact]
    public void A_snapshot_recorded_before_the_case_was_edited_is_stale()
    {
        WriteCase("default");
        WriteSnapshot("default", "Staging", DateTime.UtcNow.AddDays(-1));

        Assert.Equal(SnapshotState.Stale, Scan().Children.Single().Snapshots);
    }

    /// <summary>The ENDPOINT's URL decides what was sent just as much as the case's parameters do, so
    /// editing it makes every snapshot under it stale.</summary>
    [Fact]
    public void Editing_the_endpoint_makes_its_cases_snapshots_stale()
    {
        WriteCase("default");
        WriteSnapshot("default", "Staging", DateTime.UtcNow.AddMinutes(5));

        File.SetLastWriteTimeUtc(Path.Combine(Endpoint, "endpoint.json"), DateTime.UtcNow.AddMinutes(10));

        Assert.Equal(SnapshotState.Stale, Scan().Children.Single().Snapshots);
    }

    /// <summary>Worst-first, because the stale one is what a reader has to go and look at.</summary>
    [Fact]
    public void An_endpoint_takes_the_worst_of_its_cases()
    {
        WriteCase("default");
        WriteCase("not-found");
        WriteSnapshot("default", "Staging", DateTime.UtcNow.AddMinutes(5));
        WriteSnapshot("not-found", "Staging", DateTime.UtcNow.AddDays(-1));

        Assert.Equal(SnapshotState.Stale, Scan().Snapshots);
    }

    [Fact]
    public void An_endpoint_whose_cases_are_all_recorded_is_recorded()
    {
        WriteCase("default");
        WriteCase("not-found");
        WriteSnapshot("default", "Staging", DateTime.UtcNow.AddMinutes(5));
        WriteSnapshot("not-found", "Staging", DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(SnapshotState.Recorded, Scan().Snapshots);
    }

    /// <summary>An endpoint with no cases is sent as it stands, and its snapshots sit directly under
    /// snapshots/ rather than in a per-case directory.</summary>
    [Fact]
    public void An_endpoint_with_no_cases_reports_its_own_snapshots()
    {
        var path = Path.Combine(Endpoint, "snapshots", "Staging.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"status":200}""");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(SnapshotState.Recorded, Scan().Snapshots);
    }
}
