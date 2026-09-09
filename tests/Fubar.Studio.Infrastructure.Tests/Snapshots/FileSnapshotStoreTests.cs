using System.Text.Json.Nodes;
using Fubar.Studio.Core.Snapshots;
using Fubar.Studio.Infrastructure.Snapshots;

namespace Fubar.Studio.Infrastructure.Tests.Snapshots;

/// <summary>
/// Snapshots on disk. The rule that matters is the lookup order - a per-environment snapshot wins for
/// its environment, the shared one serves the rest - and that every answer says which file it came
/// from, because that choice is invisible in a green result otherwise.
/// </summary>
public class FileSnapshotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fubar-snap-" + Guid.NewGuid().ToString("n"));
    private readonly FileSnapshotStore _store = new();

    private string RequestPath => Path.Combine(_root, "collections", "Get order.json");

    public FileSnapshotStoreTests() =>
        Directory.CreateDirectory(Path.Combine(_root, "collections"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private ResponseSnapshot Snapshot(string? environment, string marker) => new()
    {
        Environment = environment,
        Status = 200,
        BodyFormat = "json",
        Body = JsonNode.Parse($$"""{"from":"{{marker}}"}"""),
    };

    [Fact]
    public async Task Nothing_recorded_is_reported_as_nothing_rather_than_an_empty_snapshot()
    {
        var lookup = await _store.FindAsync(_root, RequestPath, "Staging");

        Assert.False(lookup.Found);
        Assert.Null(lookup.Source);
    }

    [Fact]
    public async Task A_snapshot_survives_a_round_trip()
    {
        await _store.SaveAsync(_root, RequestPath, Snapshot("Staging", "staging"));

        var lookup = await _store.FindAsync(_root, RequestPath, "Staging");

        Assert.True(lookup.Found);
        Assert.Contains("staging", lookup.Snapshot!.BodyForComparison(), StringComparison.Ordinal);
    }

    /// <summary>Specific beats general, as everywhere else in this format.</summary>
    [Fact]
    public async Task A_per_environment_snapshot_wins_over_the_shared_one()
    {
        await _store.SaveAsync(_root, RequestPath, Snapshot(null, "shared"));
        await _store.SaveAsync(_root, RequestPath, Snapshot("Staging", "staging"));

        var staging = await _store.FindAsync(_root, RequestPath, "Staging");
        var production = await _store.FindAsync(_root, RequestPath, "Production");

        Assert.Contains("staging", staging.Snapshot!.BodyForComparison(), StringComparison.Ordinal);
        Assert.Contains("shared", production.Snapshot!.BodyForComparison(), StringComparison.Ordinal);
    }

    /// <summary>Which file answered is reported, because a run that quietly switched from the shared
    /// snapshot to a per-environment one is a run whose green means something different.</summary>
    [Fact]
    public async Task The_lookup_names_the_file_it_used()
    {
        await _store.SaveAsync(_root, RequestPath, Snapshot(null, "shared"));

        var lookup = await _store.FindAsync(_root, RequestPath, "Production");

        Assert.Equal("collections/Get order.snapshots/_shared.json", lookup.Source);
    }

    [Fact]
    public async Task Recording_again_replaces_rather_than_accumulating()
    {
        await _store.SaveAsync(_root, RequestPath, Snapshot("Staging", "first"));
        await _store.SaveAsync(_root, RequestPath, Snapshot("Staging", "second"));

        var lookup = await _store.FindAsync(_root, RequestPath, "Staging");

        Assert.Contains("second", lookup.Snapshot!.BodyForComparison(), StringComparison.Ordinal);
        Assert.Equal(["Staging"], await _store.ScopesAsync(_root, RequestPath));
    }

    /// <summary>A snapshot that will not parse is treated as absent - which reports "no snapshot"
    /// rather than failing the run with a stack trace, and still lets every other request answer.</summary>
    [Fact]
    public async Task A_corrupt_snapshot_reads_as_absent()
    {
        await _store.SaveAsync(_root, RequestPath, Snapshot("Staging", "ok"));

        var file = Directory.EnumerateFiles(
            Path.Combine(_root, "collections", "Get order.snapshots")).Single();
        await File.WriteAllTextAsync(file, "{ not json");

        var lookup = await _store.FindAsync(_root, RequestPath, "Staging");

        Assert.False(lookup.Found);
    }

    [Fact]
    public async Task An_environment_name_with_path_characters_still_writes_one_file()
    {
        await _store.SaveAsync(_root, RequestPath, Snapshot("staging/eu", "eu"));

        var lookup = await _store.FindAsync(_root, RequestPath, "staging/eu");

        Assert.True(lookup.Found);
    }
}
