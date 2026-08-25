using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>The workspace's reusable auth profiles (<c>auth-profiles.json</c>).</summary>
public interface IAuthProfileStore
{
    /// <summary>Loads <paramref name="rootPath"/>'s <c>auth-profiles.json</c>. Empty if it doesn't exist.</summary>
    Task<IReadOnlyList<AuthProfile>> LoadAuthProfilesAsync(string rootPath, CancellationToken cancellationToken = default);

    Task SaveAuthProfilesAsync(string rootPath, IReadOnlyList<AuthProfile> profiles, CancellationToken cancellationToken = default);
}
