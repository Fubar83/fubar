using System.Text.Json;
using System.Text.Json.Serialization;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Tests.Json;

/// <summary>
/// Every save used to be File.Create followed by SerializeAsync - which destroys the old file before
/// it has written the new one. A crash, a full disk, or two windows saving the same request left a
/// zero-length or half-written request.json where a valid one used to be, and these are the user's
/// committed source files rather than scratch state.
/// </summary>
public class JsonFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "fubar-atomic-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string Path_(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task A_document_round_trips()
    {
        var path = Path_("thing.json");

        await JsonFile.WriteAtomicAsync(path, new Thing { Name = "Ada" }, FubarJson.Options);

        Assert.Equal("Ada", JsonSerializer.Deserialize<Thing>(await File.ReadAllTextAsync(path), FubarJson.Options)!.Name);
    }

    [Fact]
    public async Task The_directory_is_created_if_missing()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "thing.json");

        await JsonFile.WriteAtomicAsync(path, new Thing { Name = "x" }, FubarJson.Options);

        Assert.True(File.Exists(path));
    }

    /// <summary>The guarantee. A serializer that throws part-way must leave what was already on disk
    /// exactly as it was.</summary>
    [Fact]
    public async Task A_failed_write_leaves_the_previous_file_intact()
    {
        var path = Path_("thing.json");
        await JsonFile.WriteAtomicAsync(path, new Thing { Name = "original" }, FubarJson.Options);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            JsonFile.WriteAtomicAsync(path, new Exploding(), FubarJson.Options));

        Assert.Equal("original", JsonSerializer.Deserialize<Thing>(await File.ReadAllTextAsync(path), FubarJson.Options)!.Name);
    }

    /// <summary>And leaves no temporary file behind for the next reader to trip over.</summary>
    [Fact]
    public async Task A_failed_write_cleans_up_after_itself()
    {
        var path = Path_("thing.json");
        await JsonFile.WriteAtomicAsync(path, new Thing { Name = "original" }, FubarJson.Options);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            JsonFile.WriteAtomicAsync(path, new Exploding(), FubarJson.Options));

        Assert.Equal(["thing.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void The_synchronous_form_behaves_the_same()
    {
        var path = Path_("thing.json");

        JsonFile.WriteAtomic(path, new Thing { Name = "sync" }, FubarJson.Options);

        Assert.Equal("sync", JsonSerializer.Deserialize<Thing>(File.ReadAllText(path), FubarJson.Options)!.Name);
    }

    private sealed class Thing
    {
        public string Name { get; set; } = "";
    }

    /// <summary>Throws from inside serialisation, which is where a real failure (a full disk) lands.</summary>
    private sealed class Exploding
    {
        [JsonInclude]
        public string Boom => throw new InvalidOperationException("disk full");
    }
}
