using System.Text.Json;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.History;

/// <summary>
/// Persists <see cref="ExecutionSnapshot"/> ledgers to <c>.fubar/history/&lt;requestId&gt;.json</c>
/// under the workspace root - deliberately outside <c>collections/</c> so history never gets
/// committed alongside request definitions (RequestEditorPane.md §6).
/// </summary>
public sealed class HistoryService : IHistoryService
{
    /// <summary>Default executions kept per request; the user setting overrides it.</summary>
    public const int DefaultMaxEntriesPerRequest = 200;

    private readonly IAppSettingsService? _settings;

    public HistoryService()
    {
    }

    /// <summary>The settings-aware form. Optional so the many tests that construct this directly, and
    /// the CLI which never records history, need not supply one.</summary>
    public HistoryService(IAppSettingsService settings) => _settings = settings;

    private HistorySettings Limits => _settings?.Load().History ?? new HistorySettings();

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

    /// <summary>
    /// One append at a time per ledger file.
    ///
    /// <para>Appending is read-modify-write, so two windows sending the same request interleaved would
    /// both read the same list and the second write would drop the first's entry. Per PATH rather than
    /// one global lock, so a run of twenty different requests does not serialise on the slowest of
    /// them. Same-process is enough: the ledger is local-machine state by design.</para>
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Locks =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task AppendAsync(string workspaceRootPath, string requestId, ExecutionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var path = GetPath(workspaceRootPath, requestId);
        var gate = Locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = (await LoadAsync(workspaceRootPath, requestId, cancellationToken)).ToList();
            existing.Insert(0, snapshot);

            // At least one, whatever the setting says: a cap of zero would mean appending an entry and
            // immediately dropping it, which is a slower way of recording nothing. Turning history OFF
            // is HistorySettings.Enabled's job, and it is checked before we get here.
            var max = Math.Max(1, Limits.MaxEntriesPerRequest);
            if (existing.Count > max)
            {
                existing.RemoveRange(max, existing.Count - max);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            EnsureSelfIgnored(workspaceRootPath);
            await JsonFile.WriteAtomicAsync(path, existing, FubarJson.Options, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Writes <c>.fubar/.gitignore</c> containing <c>*</c>, so the history directory excludes itself.
    ///
    /// <para>The second of two independent guards - <c>WorkspaceService.EnsureHistoryIsIgnoredAsync</c>
    /// is the first. This one matters because a workspace is often NOT the repository root: a rule
    /// written into the workspace's own .gitignore does nothing about a repository two directories
    /// above it, whereas a .gitignore inside the ignored directory works wherever it sits.</para>
    /// </summary>
    private static void EnsureSelfIgnored(string workspaceRootPath)
    {
        try
        {
            var path = Path.Combine(workspaceRootPath, ".fubar", ".gitignore");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "# Execution history: local to this machine, never committed.\n*\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Never fail a send over this. AppendAsync's own caller already treats a history failure as
            // reportable-but-not-fatal, and this is a smaller thing than that.
        }
    }

    private static string GetPath(string workspaceRootPath, string requestId) =>
        Path.Combine(workspaceRootPath, ".fubar", "history", $"{requestId}.json");
}
