using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Tests;

// The roles a run touches but does not exercise: it only ever READS a request, resolves an empty
// inheritance chain, and loads the workspace's auth profiles once. Shared by every runner test so
// the single-environment and paired runs are measured against exactly the same doubles.
internal sealed class FakeStore : IRequestStore
{
    /// <summary>No migration happens in a fake store, so nothing ever raises this.</summary>
    public event Action<string, IReadOnlyList<string>>? RequestMigrated { add { } remove { } }
    private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);

    public FakeStore FailOn(string path) { _failing.Add(path); return this; }

    public Task<RequestModel> LoadRequestAsync(string path, CancellationToken ct = default)
    {
        if (_failing.Contains(path))
        {
            throw new InvalidDataException("unexpected token");
        }

        // The folder name is the request name: /w/collections/r2/request.json -> r2
        var name = Path.GetFileName(Path.GetDirectoryName(path))!;
        return Task.FromResult(new RequestModel { Name = name, Url = "https://example.test/" });
    }

    // The rest of the role. A run only ever READS a request, so anything the runner calls here is a
    // bug rather than something to give a plausible answer to.
    public Task SaveRequestAsync(string path, RequestModel request, CancellationToken ct = default) => throw new NotSupportedException();

    public IReadOnlyList<WorkspaceTreeNode> BuildCollectionsTree(string rootPath) => throw new NotSupportedException();

    public string CreateRequest(string parentDirectory, string requestName) => throw new NotSupportedException();

    public string CreateFolder(string parentDirectory, string folderName) => throw new NotSupportedException();

    public string DuplicatePath(string path) => throw new NotSupportedException();

    public string RenamePath(string path, string newName) => throw new NotSupportedException();

    public void DeletePath(string path) => throw new NotSupportedException();
}

internal sealed class FakeInheritance : IInheritanceResolver
{
    public Task<InheritanceChain> GetInheritanceChainAsync(string root, string requestFilePath, CancellationToken ct = default) =>
        Task.FromResult(new InheritanceChain([], null, null, Array.Empty<ComparisonSettingsLayer>()));
}

internal sealed class FakeProfiles : IAuthProfileStore
{
    public int Loads { get; private set; }

    public Task<IReadOnlyList<AuthProfile>> LoadAuthProfilesAsync(string root, CancellationToken ct = default)
    {
        Loads++;
        return Task.FromResult<IReadOnlyList<AuthProfile>>([]);
    }

    public Task SaveAuthProfilesAsync(string rootPath, IReadOnlyList<AuthProfile> profiles, CancellationToken ct = default) => throw new NotSupportedException();
}
