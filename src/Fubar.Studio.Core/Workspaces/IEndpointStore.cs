using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>One case file: its name and where it lives.</summary>
/// <param name="Name">The file name without its extension, which is what the case is called.</param>
public sealed record CaseSummary(string Name, string FilePath);

/// <summary>
/// The endpoint half of the collections tree: cases, and the directory conventions around them.
/// </summary>
/// <remarks>
/// <para>Separate from <see cref="IRequestStore"/>, which still owns <c>endpoint.json</c> itself - an
/// endpoint IS a <see cref="RequestModel"/>, stored under a different name, so giving it a second
/// loader would mean two code paths for one document and two chances to disagree about its
/// format.</para>
/// <para>Only meaningful in a workspace whose <c>fubar.json</c> declares
/// <see cref="WorkspaceFormat.Endpoints"/>.</para>
/// </remarks>
public interface IEndpointStore
{
    /// <summary>The file name an endpoint directory is recognised by.</summary>
    const string EndpointFileName = "endpoint.json";

    /// <summary>Reserved directory names inside an endpoint. Neither is a folder.</summary>
    const string CasesDirName = "cases";

    const string SnapshotsDirName = "snapshots";

    /// <summary>Whether this directory is an endpoint - i.e. holds an <c>endpoint.json</c>.</summary>
    bool IsEndpoint(string directory);

    /// <summary>The endpoint directory containing <paramref name="path"/>, or null if it is not
    /// inside one. Takes an endpoint directory, an <c>endpoint.json</c> or a case file.</summary>
    string? EndpointDirectoryOf(string path);

    /// <summary>This endpoint's cases, in file-name order. Empty when it has none, which is a
    /// legitimate state: an endpoint with no cases is sent as it stands.</summary>
    IReadOnlyList<CaseSummary> ListCases(string endpointDirectory);

    Task<EndpointCase> LoadCaseAsync(string caseFilePath, CancellationToken cancellationToken = default);

    Task SaveCaseAsync(string caseFilePath, EndpointCase endpointCase, CancellationToken cancellationToken = default);

    /// <summary>
    /// A free path for a case named <paramref name="caseName"/>, writing NOTHING.
    /// </summary>
    /// <remarks>
    /// What a draft is addressed by. A new case is held in memory until it is saved, so the file it
    /// WOULD occupy has to be reserved without creating it - otherwise "New case" leaves a
    /// <c>new-case.json</c> behind on disk every time someone opens one and changes their mind.
    /// </remarks>
    string ProposeCasePath(string endpointDirectory, string caseName);

    /// <summary>
    /// Renames a case and returns its new path.
    /// </summary>
    /// <remarks>
    /// A case's name IS its file name: <c>get-order#not-found</c> resolves against the tree, and the
    /// tree takes a case's name from its file. A case whose two names disagree is one that nothing can
    /// select by the name it displays - the same rule, and the same reason, as
    /// <see cref="IBatchStore.RenameBatch"/>. Throws when a case by that name already exists.
    /// </remarks>
    string RenameCase(string caseFilePath, string newName);
}
