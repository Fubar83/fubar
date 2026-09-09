namespace Fubar.Studio.Core.Workspaces;

/// <summary>The kinds of document a workspace holds, and how to recognise one from its path.</summary>
public enum WorkspaceFileKind
{
    /// <summary>Not one of ours.</summary>
    Unknown,

    /// <summary><c>fubar.json</c> at the workspace root.</summary>
    Manifest,

    /// <summary>A <c>request.json</c> under <c>collections/</c>.</summary>
    Request,

    /// <summary>One of <c>environments/*.json</c>.</summary>
    Environment,

    /// <summary><c>auth-profiles.json</c> at the workspace root.</summary>
    AuthProfiles,

    /// <summary>A <c>_folder.json</c> inside a collections subfolder.</summary>
    FolderConfig,

    /// <summary>One <c>cases/&lt;name&gt;.json</c> inside an endpoint.</summary>
    Case,

    /// <summary>A recorded response under <c>snapshots/</c> or <c>&lt;name&gt;.snapshots/</c>.</summary>
    Snapshot,

    /// <summary>One <c>batches/&lt;name&gt;.json</c>.</summary>
    Batch,
}

/// <summary>
/// Works out which schema a workspace file should be validated against, from its path alone.
///
/// <para>Path-based because that is how the files are already distinguished everywhere else - a
/// request is any <c>.json</c> under <c>collections/</c> that is not <c>_folder.json</c>, which is
/// exactly the rule <c>WorkspaceService.ScanDirectory</c> uses. Sniffing the contents instead would
/// give a second, disagreeing answer.</para>
/// </summary>
public static class WorkspaceFiles
{
    public static WorkspaceFileKind KindOf(string workspaceRoot, string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(workspaceRoot);
        var name = Path.GetFileName(full);

        if (string.Equals(name, "fubar.json", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceFileKind.Manifest;
        }

        if (string.Equals(name, "auth-profiles.json", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceFileKind.AuthProfiles;
        }

        if (string.Equals(name, "_folder.json", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceFileKind.FolderConfig;
        }

        if (IsUnder(root, "environments", full))
        {
            return WorkspaceFileKind.Environment;
        }

        if (IsUnder(root, IBatchStore.BatchesDirName, full)
            && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceFileKind.Batch;
        }

        if (IsUnder(root, "collections", full) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            // Inside an endpoint, the directory says what a file is. Without this every case and every
            // recorded snapshot would be validated as a request and reported as a malformed one -
            // which is what a rule that says "any .json under collections" grows into once the tree
            // has more than one kind of thing in it.
            return DirectoryName(full) switch
            {
                IEndpointStore.CasesDirName => WorkspaceFileKind.Case,
                IEndpointStore.SnapshotsDirName => WorkspaceFileKind.Snapshot,
                { } directory when directory.EndsWith(".snapshots", StringComparison.OrdinalIgnoreCase) =>
                    WorkspaceFileKind.Snapshot,

                // A per-case snapshot lives one level deeper: snapshots/<case>/<environment>.json.
                _ when IsUnderSnapshots(full) => WorkspaceFileKind.Snapshot,
                _ => WorkspaceFileKind.Request,
            };
        }

        return WorkspaceFileKind.Unknown;
    }

    private static string? DirectoryName(string fullPath) =>
        Path.GetFileName(Path.GetDirectoryName(fullPath));

    private static bool IsUnderSnapshots(string fullPath)
    {
        for (var directory = Path.GetDirectoryName(fullPath);
             directory is not null;
             directory = Path.GetDirectoryName(directory))
        {
            var segment = Path.GetFileName(directory);

            if (string.Equals(segment, IEndpointStore.SnapshotsDirName, StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".snapshots", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(segment, "collections", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The schema file name for a kind, or null for <see cref="WorkspaceFileKind.Unknown"/>.</summary>
    public static string? SchemaFor(WorkspaceFileKind kind) => kind switch
    {
        WorkspaceFileKind.Manifest => "app.schema.json",
        WorkspaceFileKind.Request => "request.schema.json",
        WorkspaceFileKind.Environment => "environment.schema.json",
        WorkspaceFileKind.AuthProfiles => "auth-profiles.schema.json",
        WorkspaceFileKind.FolderConfig => "folder.schema.json",
        WorkspaceFileKind.Case => "case.schema.json",
        WorkspaceFileKind.Batch => "batch.schema.json",
        WorkspaceFileKind.Snapshot => "snapshot.schema.json",
        _ => null,
    };

    /// <summary>
    /// Every file in the workspace that has a schema, in a stable order.
    ///
    /// <para><c>.fubar/</c> is skipped: it is local execution history, not part of the format, and
    /// validating a hundred response ledgers on every run would be slow and pointless.</para>
    /// </summary>
    public static IEnumerable<(string Path, WorkspaceFileKind Kind)> Enumerate(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);

        foreach (var path in Directory
            .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(p => !IsUnder(root, ".fubar", Path.GetFullPath(p)))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var kind = KindOf(root, path);
            if (kind != WorkspaceFileKind.Unknown)
            {
                yield return (path, kind);
            }
        }
    }

    private static bool IsUnder(string root, string directoryName, string fullPath) =>
        fullPath.StartsWith(
            Path.Combine(root, directoryName) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
