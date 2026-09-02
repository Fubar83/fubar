using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>Workspace discovery and the root <c>fubar.json</c> manifest.</summary>
public interface IWorkspaceStore
{
    /// <summary>True if <paramref name="directoryPath"/> contains a <c>fubar.json</c>.</summary>
    bool IsWorkspaceRoot(string directoryPath);

    Task<Workspace> LoadWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an empty directory up as a workspace and returns it loaded: the <c>fubar.json</c>
    /// manifest, the <c>collections/</c> and <c>environments/</c> folders, and a <c>.gitignore</c>
    /// for the local-only history.
    ///
    /// A directory that is ALREADY a workspace is opened untouched rather than reinitialised - the
    /// commonest way to reach this is picking the wrong folder in a browse dialog, and rewriting
    /// someone's manifest because they did would be the worst possible response to a misclick.
    ///
    /// Here rather than in the view model that used to do it: what a new workspace consists of is a
    /// fact about the format, needed by anything that creates one, and it could not be tested at all
    /// while it lived in a click handler.
    /// </summary>
    Task<Workspace> CreateWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default);

    Task SaveAppManifestAsync(string rootPath, AppManifest manifest, CancellationToken cancellationToken = default);
}
