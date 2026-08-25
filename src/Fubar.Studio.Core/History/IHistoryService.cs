using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.History;

/// <summary>
/// Append-only execution ledger backing the Request Editor's History tab (RequestEditorPane.md §6):
/// stored at <c>.fubar/history/&lt;requestId&gt;.json</c>, outside <c>collections/</c> so it's never
/// committed to Git alongside the request definitions themselves.
/// </summary>
public interface IHistoryService
{
    Task<IReadOnlyList<ExecutionSnapshot>> LoadAsync(string workspaceRootPath, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Prepends <paramref name="snapshot"/> (newest first) and persists the ledger.</summary>
    Task AppendAsync(string workspaceRootPath, string requestId, ExecutionSnapshot snapshot, CancellationToken cancellationToken = default);
}
