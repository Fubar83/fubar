using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>The workspace's <c>environments/*.json</c> variable sets.</summary>
public interface IEnvironmentStore
{
    /// <summary>Loads every <c>environments/*.json</c> under <paramref name="rootPath"/>. Empty if the directory doesn't exist.</summary>
    Task<IReadOnlyList<WorkspaceEnvironment>> LoadEnvironmentsAsync(string rootPath, CancellationToken cancellationToken = default);

    /// <summary>Saves <paramref name="environment"/> to <c>environments/{Id}.json</c> - filed by its
    /// stable Id (not its Name), so renaming overwrites the same file instead of orphaning the old one.</summary>
    Task SaveEnvironmentAsync(string rootPath, WorkspaceEnvironment environment, CancellationToken cancellationToken = default);

    Task DeleteEnvironmentAsync(string rootPath, string environmentId, CancellationToken cancellationToken = default);
}
