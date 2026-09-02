using System.Text.Json;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Workspaces;

public sealed class WorkspaceService : IWorkspaceService
{
    private const string AppManifestFileName = "fubar.json";
    private const string AuthFileName = "auth.json";
    private const string CollectionsDirName = "collections";
    private const string RequestFileExtension = ".json";
    private const string EnvironmentsDirName = "environments";
    private const string AuthProfilesFileName = "auth-profiles.json";
    private const string FolderConfigFileName = "_folder.json";

    public bool IsWorkspaceRoot(string directoryPath) =>
        File.Exists(Path.Combine(directoryPath, AppManifestFileName));

    public async Task<Workspace> LoadWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(rootPath, AppManifestFileName);
        AppManifest manifest;
        await using (var manifestStream = File.OpenRead(manifestPath))
        {
            manifest = await JsonSerializer.DeserializeAsync<AppManifest>(manifestStream, FubarJson.Options, cancellationToken)
                ?? throw new InvalidDataException($"\"{manifestPath}\" did not deserialize to a valid fubar.json.");
        }

        AuthConfig? defaultAuth = null;
        var authPath = Path.Combine(rootPath, AuthFileName);
        if (File.Exists(authPath))
        {
            await using var authStream = File.OpenRead(authPath);
            defaultAuth = await JsonSerializer.DeserializeAsync<AuthConfig>(authStream, FubarJson.Options, cancellationToken);
        }

        return new Workspace
        {
            RootPath = rootPath,
            Manifest = manifest,
            DefaultAuth = defaultAuth,
        };
    }

    public async Task SaveAppManifestAsync(string rootPath, AppManifest manifest, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootPath);
        var manifestPath = Path.Combine(rootPath, AppManifestFileName);
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, FubarJson.Options, cancellationToken);
    }

    public async Task<Workspace> CreateWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        // Already a workspace: open it as it stands. The commonest way to arrive here is browsing to
        // the wrong folder, and rewriting someone's manifest because of a misclick is unrecoverable
        // in a way that opening the wrong workspace is not.
        if (!IsWorkspaceRoot(rootPath))
        {
            var manifest = new AppManifest { Name = new DirectoryInfo(rootPath).Name };

            await SaveAppManifestAsync(rootPath, manifest, cancellationToken);

            // Both folders, though saving either kind of file would create its own on demand. An
            // empty workspace whose LAYOUT is visible is what makes "these are ordinary files you can
            // commit" legible from the first second rather than after the first save - which is the
            // whole pitch of keeping a workspace in Git.
            Directory.CreateDirectory(Path.Combine(rootPath, CollectionsDirName));
            Directory.CreateDirectory(Path.Combine(rootPath, EnvironmentsDirName));

            // .fubar/ is execution history - local-machine scratch state, and the one thing here that
            // should NOT be committed beside the collections and environments.
            var gitignorePath = Path.Combine(rootPath, ".gitignore");

            if (!File.Exists(gitignorePath))
            {
                await File.WriteAllTextAsync(gitignorePath, ".fubar/\n", cancellationToken);
            }
        }

        return await LoadWorkspaceAsync(rootPath, cancellationToken);
    }

    public async Task<RequestModel> LoadRequestAsync(string requestFilePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(requestFilePath);
        return await JsonSerializer.DeserializeAsync<RequestModel>(stream, FubarJson.Options, cancellationToken)
            ?? throw new InvalidDataException($"\"{requestFilePath}\" did not deserialize to a valid request.json.");
    }

    public async Task SaveRequestAsync(string requestFilePath, RequestModel request, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(requestFilePath)!);
        await using var stream = File.Create(requestFilePath);
        await JsonSerializer.SerializeAsync(stream, request, FubarJson.Options, cancellationToken);
    }

    public IReadOnlyList<WorkspaceTreeNode> BuildCollectionsTree(string rootPath)
    {
        var collectionsPath = Path.Combine(rootPath, CollectionsDirName);
        return Directory.Exists(collectionsPath) ? ScanDirectory(collectionsPath) : [];
    }

    private IReadOnlyList<WorkspaceTreeNode> ScanDirectory(string directoryPath)
    {
        var nodes = new List<WorkspaceTreeNode>();

        foreach (var dir in Directory.EnumerateDirectories(directoryPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            nodes.Add(new WorkspaceTreeNode(Path.GetFileName(dir), dir, true, ScanDirectory(dir)));
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, $"*{RequestFileExtension}").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            // _folder.json is per-folder config (headers/auth inheritance), not a request - see
            // LoadFolderConfigAsync/SaveFolderConfigAsync.
            if (string.Equals(Path.GetFileName(file), FolderConfigFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            nodes.Add(new WorkspaceTreeNode(Path.GetFileName(file), file, false, [], TryReadRequestSummary(file)));
        }

        return nodes;
    }

    // Instance-level (WorkspaceService is a DI singleton) cache keyed by path + last-write time,
    // so a tree refresh triggered by an unrelated file-system event doesn't re-deserialize every
    // request.json in the workspace just to read its method/auth badge.
    private readonly Dictionary<string, (DateTime MtimeUtc, RequestSummary? Summary)> _requestSummaryCache = [];

    private RequestSummary? TryReadRequestSummary(string filePath)
    {
        DateTime mtimeUtc;
        try
        {
            mtimeUtc = File.GetLastWriteTimeUtc(filePath);
        }
        catch (IOException)
        {
            return null;
        }

        if (_requestSummaryCache.TryGetValue(filePath, out var cached) && cached.MtimeUtc == mtimeUtc)
        {
            return cached.Summary;
        }

        RequestSummary? summary = null;
        try
        {
            using var stream = File.OpenRead(filePath);
            var request = JsonSerializer.Deserialize<RequestModel>(stream, FubarJson.Options);
            if (request is not null)
            {
                summary = new RequestSummary(request.Method, request.Auth.Type != AuthType.Inherit);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Malformed or mid-write request.json (e.g. the app is saving it right now) - just
            // show no badges for this scan rather than failing the whole tree.
        }

        _requestSummaryCache[filePath] = (mtimeUtc, summary);
        return summary;
    }

    public string CreateRequest(string parentDirectory, string requestName)
    {
        Directory.CreateDirectory(parentDirectory);
        var fileName = SanitizeFileName(requestName) + RequestFileExtension;
        var path = UniquePath(parentDirectory, fileName);

        var request = new RequestModel { Name = requestName };
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, request, FubarJson.Options);

        return path;
    }

    public string CreateFolder(string parentDirectory, string folderName)
    {
        Directory.CreateDirectory(parentDirectory);
        var path = UniquePath(parentDirectory, SanitizeFileName(folderName));
        Directory.CreateDirectory(path);
        return path;
    }

    public string DuplicatePath(string path)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"\"{path}\" has no parent directory.");

        if (Directory.Exists(path))
        {
            var destination = UniquePath(parent, $"{Path.GetFileName(path)} (copy)");
            CopyDirectory(path, destination);
            return destination;
        }

        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var filePath = UniquePath(parent, $"{nameWithoutExt} (copy){ext}");
        File.Copy(path, filePath);
        return filePath;
    }

    public string RenamePath(string path, string newName)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"\"{path}\" has no parent directory.");
        var isDirectory = Directory.Exists(path);
        var sanitized = SanitizeFileName(newName);

        var destinationName = isDirectory || Path.HasExtension(sanitized)
            ? sanitized
            : sanitized + Path.GetExtension(path);

        var destination = Path.Combine(parent, destinationName);
        if (string.Equals(destination, path, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (isDirectory)
        {
            Directory.Move(path, destination);
        }
        else
        {
            File.Move(path, destination);
        }

        return destination;
    }

    public void DeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
        }
    }

    private static string UniquePath(string parentDirectory, string desiredName)
    {
        var candidate = Path.Combine(parentDirectory, desiredName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var nameWithoutExt = Path.GetFileNameWithoutExtension(desiredName);
        var ext = Path.GetExtension(desiredName);

        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(parentDirectory, $"{nameWithoutExt} {i}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }

    public async Task<IReadOnlyList<WorkspaceEnvironment>> LoadEnvironmentsAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(rootPath, EnvironmentsDirName);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var environments = new List<WorkspaceEnvironment>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(file);
            var environment = await JsonSerializer.DeserializeAsync<WorkspaceEnvironment>(stream, FubarJson.Options, cancellationToken);
            if (environment is not null)
            {
                environments.Add(environment);
            }
        }

        return environments;
    }

    public async Task SaveEnvironmentAsync(string rootPath, WorkspaceEnvironment environment, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(rootPath, EnvironmentsDirName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{environment.Id}.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, environment, FubarJson.Options, cancellationToken);
    }

    public Task DeleteEnvironmentAsync(string rootPath, string environmentId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(rootPath, EnvironmentsDirName, $"{environmentId}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AuthProfile>> LoadAuthProfilesAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(rootPath, AuthProfilesFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<AuthProfile>>(stream, FubarJson.Options, cancellationToken) ?? [];
    }

    public async Task SaveAuthProfilesAsync(string rootPath, IReadOnlyList<AuthProfile> profiles, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootPath);
        var path = Path.Combine(rootPath, AuthProfilesFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profiles, FubarJson.Options, cancellationToken);
    }

    public async Task<FolderConfig> LoadFolderConfigAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(folderPath, FolderConfigFileName);
        if (!File.Exists(path))
        {
            return new FolderConfig();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<FolderConfig>(stream, FubarJson.Options, cancellationToken) ?? new FolderConfig();
    }

    public async Task SaveFolderConfigAsync(string folderPath, FolderConfig config, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folderPath);
        var path = Path.Combine(folderPath, FolderConfigFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, config, FubarJson.Options, cancellationToken);
    }

    public async Task<InheritanceChain> GetInheritanceChainAsync(string rootPath, string requestFilePath, CancellationToken cancellationToken = default)
    {
        // Walk from the request's parent folder up to (but not past) the workspace root,
        // collecting each ancestor's folder config. Reversed at the end so the root-most
        // ancestor's headers appear first - a caller layering these into a dictionary by key then
        // lets a *closer* folder's same-key header win, matching normal cascade semantics.
        var ancestors = new List<(string Path, FolderConfig Config)>();
        var normalizedRoot = Path.GetFullPath(Path.Combine(rootPath, CollectionsDirName));
        var current = Path.GetDirectoryName(Path.GetFullPath(requestFilePath));

        while (current is not null && current.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            var config = await LoadFolderConfigAsync(current, cancellationToken);
            ancestors.Add((current, config));

            if (string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }

        ancestors.Reverse();

        var headers = new List<InheritedHeader>();
        var comparisonLayers = new List<ComparisonSettingsLayer>();
        string? authProfileId = null;
        string? authSourceName = null;

        foreach (var (path, config) in ancestors)
        {
            var sourceName = string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? "Workspace Root"
                : Path.GetFileName(path);

            headers.AddRange(config.Headers.Select(h => new InheritedHeader(h, $"Folder: {sourceName}")));

            // Nearest ancestor with an explicit auth profile wins - keep overwriting as we walk
            // root-to-request order, so the last (closest) assignment is what survives.
            if (config.AuthProfileId is not null)
            {
                authProfileId = config.AuthProfileId;
                authSourceName = $"Folder: {sourceName}";
            }

            // Kept in walk order (root-most first) rather than collapsed here: the resolver picks a
            // winner per SETTING, so a nearer folder overriding one option must not discard a further
            // one's contribution to the others.
            if (config.Comparison is not null)
            {
                comparisonLayers.Add(new ComparisonSettingsLayer(
                    config.Comparison,
                    ComparisonScope.Folder,
                    $"Folder: {sourceName}"));
            }
        }

        return new InheritanceChain(headers, authProfileId, authSourceName, comparisonLayers);
    }
}
