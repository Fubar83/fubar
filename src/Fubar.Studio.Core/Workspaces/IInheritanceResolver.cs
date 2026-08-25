using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>
/// Resolves the header &amp; auth inheritance chain for a request (RequestEditorPane.md §5): walks from
/// the request's parent folder up to the workspace root, layering ancestor <c>_folder.json</c> headers
/// (root-most first) and resolving the nearest non-Inherit auth assignment.
/// </summary>
public interface IInheritanceResolver
{
    Task<InheritanceChain> GetInheritanceChainAsync(string rootPath, string requestFilePath, CancellationToken cancellationToken = default);
}

/// <summary>Result of <see cref="IInheritanceResolver.GetInheritanceChainAsync"/>: headers inherited from
/// ancestor folders (root-most first) plus whichever <see cref="AuthProfile"/> id applies, if any.</summary>
public sealed record InheritanceChain(IReadOnlyList<InheritedHeader> Headers, string? AuthProfileId, string? AuthSourceName);

/// <summary>One inherited header plus which folder (or auth profile) it came from, for the Headers tab's
/// "Source" column (RequestEditorPane.md §5).</summary>
public sealed record InheritedHeader(KeyValueItem Item, string SourceName);
