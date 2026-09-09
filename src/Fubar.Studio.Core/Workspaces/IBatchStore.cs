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

    /// <summary>
    /// Every batch belonging to <paramref name="owner"/> - the directory that HOLDS a
    /// <c>batches/</c>, not the <c>batches/</c> directory itself.
    /// </summary>
    /// <remarks>
    /// One method for both homes, because they are the same shape: the workspace root holds the
    /// cross-cutting occasions, and an endpoint directory holds the ways of running that endpoint. A
    /// second method would be two code paths for one question.
    /// </remarks>
    IReadOnlyList<BatchSummary> ListBatches(string owner);

    Task<Batch> LoadBatchAsync(string batchFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads by NAME from <paramref name="owner"/>, as a selector gives it - <c>@smoke</c> from the
    /// workspace root, <c>orders/get-order@smoke</c> from that endpoint's directory. Null when there
    /// is no such batch - reported by the caller, never treated as an empty run.
    /// </summary>
    Task<Batch?> FindBatchAsync(string owner, string name, CancellationToken cancellationToken = default);

    Task SaveBatchAsync(string batchFilePath, Batch batch, CancellationToken cancellationToken = default);

    /// <summary>Creates an empty batch named <paramref name="name"/> under <paramref name="owner"/>
    /// and returns its full path.</summary>
    string CreateBatch(string owner, string name);

    /// <inheritdoc cref="IEndpointStore.ProposeCasePath"/>
    string ProposeBatchPath(string owner, string name);

    /// <summary>
    /// Renames a batch and returns its new path.
    /// </summary>
    /// <remarks>
    /// A batch's name IS its file name - <see cref="FindBatchAsync"/> resolves <c>@smoke</c> against
    /// the directory listing - so renaming one moves a file rather than setting a field. Throws when a
    /// batch by that name already exists: quietly replacing one occasion with another is not a rename.
    /// </remarks>
    string RenameBatch(string batchFilePath, string newName);

    /// <inheritdoc cref="DocumentName.IsValid"/>
    static bool IsValidBatchName(string? name) => DocumentName.IsValid(name);
}
