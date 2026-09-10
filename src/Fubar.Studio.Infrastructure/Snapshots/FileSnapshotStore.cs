using System.Text.Json;
using Fubar.Studio.Core.Snapshots;

namespace Fubar.Studio.Infrastructure.Snapshots;

/// <summary>
/// Snapshots on disk, beside the request they were recorded from.
/// </summary>
/// <remarks>
/// <para>
/// <c>collections/Get order.json</c> keeps its snapshots in
/// <c>collections/Get order.snapshots/staging.json</c> - a sibling directory named after the request,
/// so a review of the request and a review of what it answers land in the same place, and deleting a
/// request takes its snapshots with it if you delete the folder too.
/// </para>
/// <para>
/// The endpoint format (spec §3.1) puts these in the endpoint's own directory instead. Nothing here
/// assumes either shape beyond "a path derived from the request path", so that move is a change to
/// <see cref="DirectoryFor"/> and nothing else.
/// </para>
/// </remarks>
public sealed class FileSnapshotStore : ISnapshotStore
{
    /// <summary>The shared snapshot's file name. Underscore-prefixed like <c>_folder.json</c>, which is
    /// this format's mark for a reserved name - and what stops an environment called "shared"
    /// colliding with it.</summary>
    public const string SharedFileName = "_shared";

    private const string SnapshotDirectorySuffix = ".snapshots";

    public async Task<SnapshotLookup> FindAsync(
        string workspaceRoot,
        string requestPath,
        string? environmentName,
        CancellationToken cancellationToken = default)
    {
        var directory = DirectoryFor(requestPath);

        // Specific beats general, as everywhere else in this format.
        if (environmentName is { Length: > 0 })
        {
            var own = Path.Combine(directory, FileName(environmentName));
            if (await ReadAsync(own, cancellationToken).ConfigureAwait(false) is { } scoped)
            {
                return new SnapshotLookup(scoped, Describe(own, workspaceRoot));
            }
        }

        var shared = Path.Combine(directory, FileName(SharedFileName));
        if (await ReadAsync(shared, cancellationToken).ConfigureAwait(false) is { } fallback)
        {
            return new SnapshotLookup(fallback, Describe(shared, workspaceRoot));
        }

        return SnapshotLookup.None;
    }

    public async Task SaveAsync(
        string workspaceRoot,
        string requestPath,
        ResponseSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var directory = DirectoryFor(requestPath);
        Directory.CreateDirectory(directory);

        var name = snapshot.Scope == SnapshotScope.Shared ? SharedFileName : snapshot.Environment!;
        var path = Path.Combine(directory, FileName(name));

        var json = JsonSerializer.Serialize(snapshot, SnapshotJson.Options);

        // Written through a temporary file and moved into place, like every other write in this
        // workspace: a half-written snapshot would fail every future run with a parse error, and the
        // run that produced it would already have exited.
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    public Task<IReadOnlyList<string>> ScopesAsync(
        string workspaceRoot,
        string requestPath,
        CancellationToken cancellationToken = default)
    {
        var directory = DirectoryFor(requestPath);
        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> names =
        [
            .. Directory.EnumerateFiles(directory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
        ];

        return Task.FromResult(names);
    }

    /// <summary>The sibling directory holding one request's snapshots.</summary>
    /// <summary>
    /// Where one subject's snapshots live. The subject is whichever file identifies what was sent - a
    /// <c>request.json</c>, an <c>endpoint.json</c>, or a case file - so callers say what they ran and
    /// nothing has to carry a second "which case" argument down the whole stack.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>collections/Ping.json</c> → <c>collections/Ping.snapshots/</c> (requests format)</item>
    /// <item><c>…/get-order/endpoint.json</c> → <c>…/get-order/snapshots/</c></item>
    /// <item><c>…/get-order/cases/not-found.json</c> → <c>…/get-order/snapshots/not-found/</c></item>
    /// </list>
    /// <para>A directory per case rather than one file per environment for the whole endpoint: two
    /// cases of an endpoint answer differently by design - that is what makes them two cases - so one
    /// <c>staging.json</c> between them would have each overwrite the other, and every run would
    /// report the other case's answer as a regression.</para>
    /// </remarks>
    private static string DirectoryFor(string requestPath)
    {
        var parent = Path.GetDirectoryName(requestPath)
                     ?? throw new InvalidOperationException($"\"{requestPath}\" has no parent directory.");

        if (string.Equals(
                Path.GetFileName(requestPath),
                Core.Workspaces.IEndpointStore.EndpointFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(parent, Core.Workspaces.IEndpointStore.SnapshotsDirName);
        }

        if (string.Equals(
                Path.GetFileName(parent),
                Core.Workspaces.IEndpointStore.CasesDirName,
                StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(parent) is { } endpointDirectory)
        {
            return Path.Combine(
                endpointDirectory,
                Core.Workspaces.IEndpointStore.SnapshotsDirName,
                Path.GetFileNameWithoutExtension(requestPath));
        }

        return Path.Combine(parent, Path.GetFileNameWithoutExtension(requestPath) + SnapshotDirectorySuffix);
    }

    private static string FileName(string scope) => Sanitize(scope) + ".json";

    /// <summary>An environment name is user text and can hold anything a person can type; a file name
    /// cannot - and this one is committed, so the rule is the format's rather than the host's.</summary>
    private static string Sanitize(string name) =>
        Core.Workspaces.DocumentName.Sanitize(name, "Unnamed");

    private static async Task<ResponseSnapshot?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ResponseSnapshot>(json, SnapshotJson.Options);
        }
        catch (JsonException)
        {
            // A snapshot that will not parse is treated as absent, which reports NoSnapshot rather
            // than failing the run with a stack trace. The file is the thing to fix, and the run still
            // gets to tell you about every other request.
            return null;
        }
    }

    private static string Describe(string path, string workspaceRoot) =>
        Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
}
