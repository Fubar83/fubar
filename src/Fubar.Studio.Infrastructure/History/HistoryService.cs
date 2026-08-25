using System.Text.Json;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.History;

/// <summary>
/// Persists <see cref="ExecutionSnapshot"/> ledgers to <c>.fubar/history/&lt;requestId&gt;.json</c>
/// under the workspace root - deliberately outside <c>collections/</c> so history never gets
/// committed alongside request definitions (RequestEditorPane.md §6).
/// </summary>
public sealed class HistoryService : IHistoryService
{
    private const int MaxEntriesPerRequest = 200;

    public async Task<IReadOnlyList<ExecutionSnapshot>> LoadAsync(string workspaceRootPath, string requestId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(workspaceRootPath, requestId);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ExecutionSnapshot>>(stream, FubarJson.Options, cancellationToken) ?? [];
    }

    public async Task AppendAsync(string workspaceRootPath, string requestId, ExecutionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var existing = (await LoadAsync(workspaceRootPath, requestId, cancellationToken)).ToList();
        existing.Insert(0, snapshot);
        if (existing.Count > MaxEntriesPerRequest)
        {
            existing.RemoveRange(MaxEntriesPerRequest, existing.Count - MaxEntriesPerRequest);
        }

        var path = GetPath(workspaceRootPath, requestId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, existing, FubarJson.Options, cancellationToken);
    }

    private static string GetPath(string workspaceRootPath, string requestId) =>
        Path.Combine(workspaceRootPath, ".fubar", "history", $"{requestId}.json");
}
