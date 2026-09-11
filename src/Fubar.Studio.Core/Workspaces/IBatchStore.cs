using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>One batch: its name and the DIRECTORY it is.</summary>
public sealed record BatchSummary(string Name, string DirectoryPath);

/// <summary>One item of a batch: its name and the file it is.</summary>
public sealed record BatchItemSummary(string Name, string FilePath);

/// <summary>
/// An endpoint's <c>batches/</c> directory.
/// </summary>
/// <remarks>
/// <para><b>A batch is a FOLDER, and each item in it is a file.</b> An item is one way of calling the
/// endpoint the batch belongs to - its own body, its own assertions - and belongs to that batch alone.
/// A batch was a single file listing steps that pointed elsewhere; the pointing was the part that
/// broke, and a folder of items cannot point at anything that is not in it.</para>
/// <para>The batch's own settings - what should judge it, which environments, whether to stop at the
/// first failure - live in <c>_batch.json</c> beside its items, the way a folder's settings live in
/// <c>_folder.json</c>. A batch with no <c>_batch.json</c> is a perfectly good batch that has not been
/// told anything yet.</para>
/// </remarks>
public interface IBatchStore
{
    const string BatchesDirName = "batches";

    /// <summary>The file a batch's own settings live in, beside its items.</summary>
    const string BatchFileName = "_batch.json";

    /// <summary>
    /// Every batch belonging to <paramref name="owner"/> - the directory that HOLDS a
    /// <c>batches/</c>, not the <c>batches/</c> directory itself.
    /// </summary>
    IReadOnlyList<BatchSummary> ListBatches(string owner);

    /// <summary>
    /// The items in a batch, in the order they will be sent.
    /// </summary>
    /// <remarks>
    /// Sorted by file name with <see cref="NaturalOrder"/>, so <c>request-2</c> comes before
    /// <c>request-10</c>. The file name IS the order: there is no index to keep in step with the
    /// directory, and renaming a file is how you reorder a batch.
    /// </remarks>
    IReadOnlyList<BatchItemSummary> ListItems(string batchDirectory);

    /// <summary>The batch's own settings, or defaults when it has not been told any.</summary>
    Task<Batch> LoadBatchAsync(string batchDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads by NAME from <paramref name="owner"/>, as a selector gives it - <c>@smoke</c>. Null when
    /// there is no such batch - reported by the caller, never treated as an empty run.
    /// </summary>
    Task<Batch?> FindBatchAsync(string owner, string name, CancellationToken cancellationToken = default);

    /// <summary>The directory a named batch is, or null when there is none.</summary>
    string? FindBatchDirectory(string owner, string name);

    Task SaveBatchAsync(string batchDirectory, Batch batch, CancellationToken cancellationToken = default);

    /// <summary>A directory path a new batch could take, not yet created.</summary>
    string ProposeBatchPath(string owner, string name);

    /// <summary>A file path a new item could take inside a batch, not yet created.</summary>
    string ProposeItemPath(string batchDirectory, string name);

    /// <summary>
    /// Renames a batch and returns its new directory.
    /// </summary>
    /// <remarks>
    /// A batch's name IS its directory name - <see cref="FindBatchAsync"/> resolves <c>@smoke</c>
    /// against the listing - so renaming one moves a directory rather than setting a field. Throws
    /// when a batch by that name already exists: quietly merging one occasion into another is not a
    /// rename.
    /// </remarks>
    string RenameBatch(string batchDirectory, string newName);

    /// <inheritdoc cref="DocumentName.IsValid"/>
    static bool IsValidBatchName(string? name) => DocumentName.IsValid(name);
}
