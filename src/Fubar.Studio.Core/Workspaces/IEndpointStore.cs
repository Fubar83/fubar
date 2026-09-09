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

    /// <summary>Creates an empty case named <paramref name="caseName"/> and returns its full path.</summary>
    string CreateCase(string endpointDirectory, string caseName);

    /// <summary>Creates an endpoint directory with an <c>endpoint.json</c> in it, and returns the
    /// directory.</summary>
    string CreateEndpoint(string parentDirectory, string endpointName);
}
