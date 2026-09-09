using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>One batch file: its name and where it lives.</summary>
public sealed record BatchSummary(string Name, string FilePath);

/// <summary>
/// The <c>batches/</c> directory at the workspace root.
/// </summary>
/// <remarks>
/// Beside the collection rather than inside it, because a batch is an occasion rather than a thing
/// that exists: the tree says what the API has, a batch says what to call on a particular run, and the
/// two lists change for different reasons.
/// </remarks>
public interface IBatchStore
{
    const string BatchesDirName = "batches";

    IReadOnlyList<BatchSummary> ListBatches(string workspaceRoot);

    Task<Batch> LoadBatchAsync(string batchFilePath, CancellationToken cancellationToken = default);

    /// <summary>Loads by NAME, as a selector like <c>@smoke</c> gives it. Null when there is no such
    /// batch - reported by the caller, never treated as an empty run.</summary>
    Task<Batch?> FindBatchAsync(string workspaceRoot, string name, CancellationToken cancellationToken = default);

    Task SaveBatchAsync(string batchFilePath, Batch batch, CancellationToken cancellationToken = default);

    /// <summary>Creates an empty batch named <paramref name="name"/> and returns its full path.</summary>
    string CreateBatch(string workspaceRoot, string name);

    /// <summary>
    /// Renames a batch and returns its new path.
    /// </summary>
    /// <remarks>
    /// A batch's name IS its file name - <see cref="FindBatchAsync"/> resolves <c>@smoke</c> against
    /// the directory listing - so renaming one moves a file rather than setting a field. Throws when a
    /// batch by that name already exists: quietly replacing one occasion with another is not a rename.
    /// </remarks>
    string RenameBatch(string batchFilePath, string newName);

    /// <summary>
    /// Whether <paramref name="name"/> can name a batch.
    /// </summary>
    /// <remarks>
    /// Both separators are rejected explicitly rather than left to
    /// <see cref="System.IO.Path.GetInvalidFileNameChars"/>, which on Unix reports only NUL and
    /// <c>/</c> - a name holding a backslash would pass there and produce one file on Windows and
    /// another on Linux out of the same workspace.
    /// </remarks>
    static bool IsValidBatchName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.Contains('/')
        && !name.Contains('\\')
        && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
}
