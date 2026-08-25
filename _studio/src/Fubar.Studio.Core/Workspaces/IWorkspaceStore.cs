using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>Workspace discovery and the root <c>fubar.json</c> manifest.</summary>
public interface IWorkspaceStore
{
    /// <summary>True if <paramref name="directoryPath"/> contains a <c>fubar.json</c>.</summary>
    bool IsWorkspaceRoot(string directoryPath);

    Task<Workspace> LoadWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default);

    Task SaveAppManifestAsync(string rootPath, AppManifest manifest, CancellationToken cancellationToken = default);
}
