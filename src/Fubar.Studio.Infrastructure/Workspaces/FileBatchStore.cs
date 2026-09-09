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

    public string ProposeBatchPath(string owner, string name) =>
        Unique(Path.Combine(owner, IBatchStore.BatchesDirName), name);

    /// <summary>A free file name, so creating twice makes two batches rather than overwriting the
    /// first - and so a draft reserves a name without occupying it.</summary>
    private static string Unique(string directory, string name)
    {
        var candidate = Path.Combine(directory, name + Extension);
        var n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name} {n++}{Extension}");
        }

        return candidate;
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
