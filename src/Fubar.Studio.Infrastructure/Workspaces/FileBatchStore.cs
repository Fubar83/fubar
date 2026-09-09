using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Workspaces;

/// <inheritdoc cref="IBatchStore"/>
public sealed class FileBatchStore : IBatchStore
{
    private const string Extension = ".json";

    public IReadOnlyList<BatchSummary> ListBatches(string workspaceRoot)
    {
        var directory = Path.Combine(workspaceRoot, IBatchStore.BatchesDirName);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, $"*{Extension}")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new BatchSummary(Path.GetFileNameWithoutExtension(f), f)),
        ];
    }

    public async Task<Batch> LoadBatchAsync(string batchFilePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(batchFilePath);

        var loaded = await JsonSerializer
            .DeserializeAsync<Batch>(stream, FubarJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return loaded ?? throw new InvalidDataException($"\"{batchFilePath}\" did not deserialize to a batch.");
    }

    /// <summary>
    /// By name, case-insensitively, from the listing rather than by building a path.
    /// </summary>
    /// <remarks>
    /// A name from a selector is user input, and joining it onto a directory would let
    /// <c>@../../etc/passwd</c> address a file outside the workspace. Matching against what the
    /// directory actually holds cannot leave it.
    /// </remarks>
    public async Task<Batch?> FindBatchAsync(
        string workspaceRoot,
        string name,
        CancellationToken cancellationToken = default)
    {
        var found = ListBatches(workspaceRoot)
            .FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

        return found is null
            ? null
            : await LoadBatchAsync(found.FilePath, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveBatchAsync(string batchFilePath, Batch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return JsonFile.WriteAtomicAsync(batchFilePath, batch, FubarJson.Options, cancellationToken);
    }

    public string CreateBatch(string workspaceRoot, string name)
    {
        var directory = Path.Combine(workspaceRoot, IBatchStore.BatchesDirName);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, name + Extension);
        var n = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{name} {n++}{Extension}");
        }

        var batch = new Batch { Name = Path.GetFileNameWithoutExtension(path) };
        File.WriteAllText(path, JsonSerializer.Serialize(batch, FubarJson.Options));

        return path;
    }
}
