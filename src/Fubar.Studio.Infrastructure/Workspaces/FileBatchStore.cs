using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Workspaces;

/// <inheritdoc cref="IBatchStore"/>
public sealed class FileBatchStore : IBatchStore
{
    private const string Extension = ".json";

    public IReadOnlyList<BatchSummary> ListBatches(string owner)
    {
        var directory = Path.Combine(owner, IBatchStore.BatchesDirName);
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
        string owner,
        string name,
        CancellationToken cancellationToken = default)
    {
        var found = ListBatches(owner)
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

    public string CreateBatch(string owner, string name)
    {
        var directory = Path.Combine(owner, IBatchStore.BatchesDirName);
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

    public string RenameBatch(string batchFilePath, string newName)
    {
        if (!IBatchStore.IsValidBatchName(newName))
        {
            throw new ArgumentException($"\"{newName}\" cannot name a batch.", nameof(newName));
        }

        var destination = Path.Combine(
            Path.GetDirectoryName(batchFilePath) ?? "", newName + Extension);

        // Case-only renames are a move onto "the same" file on Windows and a different one elsewhere.
        // Compared ordinally so "smoke" -> "Smoke" is still done rather than skipped.
        if (string.Equals(destination, batchFilePath, StringComparison.Ordinal))
        {
            return batchFilePath;
        }

        // File.Move's own overwrite flag defaults to false, but the check is here anyway so the
        // failure names the batch rather than the path.
        if (!string.Equals(destination, batchFilePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(destination))
        {
            throw new IOException($"There is already a batch called \"{newName}\".");
        }

        File.Move(batchFilePath, destination);

        return destination;
    }
}
