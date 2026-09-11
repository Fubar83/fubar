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
            .. NaturalOrder
                .Sort(Directory.EnumerateDirectories(directory), Path.GetFileName!)
                .Select(d => new BatchSummary(Path.GetFileName(d), d)),
        ];
    }

    /// <summary>
    /// The batch's items, naturally ordered so <c>request-2</c> comes before <c>request-10</c>.
    /// </summary>
    /// <remarks>
    /// <c>_batch.json</c> is the batch's own settings and is skipped - it is not one of the calls. The
    /// leading underscore is the same marker <c>_folder.json</c> uses, and nothing else in a batch may
    /// start with one.
    /// </remarks>
    public IReadOnlyList<BatchItemSummary> ListItems(string batchDirectory)
    {
        if (!Directory.Exists(batchDirectory))
        {
            return [];
        }

        return
        [
            .. NaturalOrder
                .Sort(
                    Directory.EnumerateFiles(batchDirectory, $"*{Extension}")
                        .Where(f => !string.Equals(
                            Path.GetFileName(f), IBatchStore.BatchFileName, StringComparison.OrdinalIgnoreCase)),
                    Path.GetFileName!)
                .Select(f => new BatchItemSummary(Path.GetFileNameWithoutExtension(f), f)),
        ];
    }

    /// <summary>
    /// A batch with no <c>_batch.json</c> is a perfectly good batch that has not been told anything
    /// yet - a folder of items and no opinion about what should judge them.
    /// </summary>
    public async Task<Batch> LoadBatchAsync(string batchDirectory, CancellationToken cancellationToken = default)
    {
        var settings = Path.Combine(batchDirectory, IBatchStore.BatchFileName);
        var name = Path.GetFileName(batchDirectory);

        if (!File.Exists(settings))
        {
            return new Batch { Name = name };
        }

        await using var stream = File.OpenRead(settings);

        var loaded = await JsonSerializer
            .DeserializeAsync<Batch>(stream, FubarJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (loaded is null)
        {
            throw new InvalidDataException($"\"{settings}\" did not deserialize to a batch.");
        }

        // The DIRECTORY's name is the batch's name - that is what a selector resolves - so a stale one
        // inside the file never gets to disagree with it.
        loaded.Name = name;
        return loaded;
    }

    /// <summary>
    /// By name, case-insensitively, from the listing rather than by building a path.
    /// </summary>
    /// <remarks>
    /// A name from a selector is user input, and joining it onto a directory would let
    /// <c>@../../etc/passwd</c> address something outside the workspace. Matching against what the
    /// directory actually holds cannot leave it.
    /// </remarks>
    public string? FindBatchDirectory(string owner, string name) =>
        ListBatches(owner)
            .FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.DirectoryPath;

    public async Task<Batch?> FindBatchAsync(
        string owner,
        string name,
        CancellationToken cancellationToken = default) =>
        FindBatchDirectory(owner, name) is { } directory
            ? await LoadBatchAsync(directory, cancellationToken).ConfigureAwait(false)
            : null;

    public Task SaveBatchAsync(string batchDirectory, Batch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        Directory.CreateDirectory(batchDirectory);

        return JsonFile.WriteAtomicAsync(
            Path.Combine(batchDirectory, IBatchStore.BatchFileName), batch, FubarJson.Options, cancellationToken);
    }

    public string ProposeBatchPath(string owner, string name) =>
        Unique(Path.Combine(owner, IBatchStore.BatchesDirName), name, Directory.Exists, "");

    public string ProposeItemPath(string batchDirectory, string name) =>
        Unique(batchDirectory, name, File.Exists, Extension);

    /// <summary>A free name, so creating twice makes two rather than overwriting the first - and so a
    /// draft reserves a name without occupying it.</summary>
    private static string Unique(string directory, string name, Func<string, bool> taken, string extension)
    {
        var candidate = Path.Combine(directory, name + extension);
        var n = 2;
        while (taken(candidate))
        {
            candidate = Path.Combine(directory, $"{name} {n++}{extension}");
        }

        return candidate;
    }

    public string RenameBatch(string batchDirectory, string newName)
    {
        if (!IBatchStore.IsValidBatchName(newName))
        {
            throw new ArgumentException($"\"{newName}\" cannot name a batch.", nameof(newName));
        }

        var destination = Path.Combine(Path.GetDirectoryName(batchDirectory) ?? "", newName);

        // Case-only renames are a move onto "the same" directory on Windows and a different one
        // elsewhere. Compared ordinally so "smoke" -> "Smoke" is still done rather than skipped.
        if (string.Equals(destination, batchDirectory, StringComparison.Ordinal))
        {
            return batchDirectory;
        }

        if (!string.Equals(destination, batchDirectory, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(destination))
        {
            throw new IOException($"There is already a batch called \"{newName}\".");
        }

        Directory.Move(batchDirectory, destination);

        return destination;
    }
}
