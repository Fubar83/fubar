using System.IO;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests;

/// <summary>A test double for <see cref="IWorkspaceService"/> that records saved requests/environments
/// and synthesizes paths, so import logic can be tested without touching real request.json files.
/// Members not needed by the importers throw.</summary>
internal sealed class RecordingWorkspaceService : IWorkspaceService
{
    public List<RequestModel> SavedRequests { get; } = [];

    public List<WorkspaceEnvironment> SavedEnvironments { get; } = [];

    public string CreateRequest(string parentDirectory, string requestName) =>
        Path.Combine(parentDirectory, requestName, "request.json");

    public Task SaveRequestAsync(string requestFilePath, RequestModel request, CancellationToken cancellationToken = default)
    {
        SavedRequests.Add(request);
        return Task.CompletedTask;
    }

    public Task SaveEnvironmentAsync(string rootPath, WorkspaceEnvironment environment, CancellationToken cancellationToken = default)
    {
        SavedEnvironments.Add(environment);
        return Task.CompletedTask;
    }

    public string CreateFolder(string parentDirectory, string folderName) => Path.Combine(parentDirectory, folderName);

    public bool IsWorkspaceRoot(string directoryPath) => throw new NotSupportedException();

    public Task<Workspace> LoadWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Workspace> CreateWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SaveAppManifestAsync(string rootPath, AppManifest manifest, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<RequestModel> LoadRequestAsync(string requestFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IReadOnlyList<WorkspaceTreeNode> BuildCollectionsTree(string rootPath) => throw new NotSupportedException();

    public string DuplicatePath(string path) => throw new NotSupportedException();

    public string RenamePath(string path, string newName) => throw new NotSupportedException();

    public void DeletePath(string path) => throw new NotSupportedException();

    public Task<IReadOnlyList<WorkspaceEnvironment>> LoadEnvironmentsAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task DeleteEnvironmentAsync(string rootPath, string environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<AuthProfile>> LoadAuthProfilesAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SaveAuthProfilesAsync(string rootPath, IReadOnlyList<AuthProfile> profiles, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<FolderConfig> LoadFolderConfigAsync(string folderPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SaveFolderConfigAsync(string folderPath, FolderConfig config, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<InheritanceChain> GetInheritanceChainAsync(string rootPath, string requestFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
