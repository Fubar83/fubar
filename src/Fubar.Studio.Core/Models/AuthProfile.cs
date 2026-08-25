namespace Fubar.Studio.Core.Models;

/// <summary>
/// A named, reusable auth definition stored in a workspace's <c>auth-profiles.json</c> - e.g.
/// "Admin OAuth", "Public API Key". A request or folder selects one by <c>Id</c>
/// (<see cref="RequestModel.AuthProfileId"/> / <see cref="FolderConfig.AuthProfileId"/>) instead of
/// duplicating the raw <see cref="AuthConfig"/> everywhere it's used.
/// </summary>
public sealed class AuthProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public required string Name { get; set; }

    public AuthConfig Config { get; set; } = new();
}
