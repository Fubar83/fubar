namespace Fubar.Studio.Core.Models;

/// <summary>
/// Runtime aggregate for a loaded workspace: its root directory plus the parsed
/// <c>fubar.json</c>/<c>auth.json</c>. Several workspaces can be open at once, each appearing as
/// its own root in the Workspace Explorer.
/// </summary>
public sealed class Workspace
{
    public required string RootPath { get; init; }

    public required AppManifest Manifest { get; init; }

    public AuthConfig? DefaultAuth { get; init; }

    /// <summary>Stable id used in the OS keyring key format <c>fubar:[WorkspaceId]:[VariableKey]</c>.</summary>
    public string WorkspaceId => Manifest.Id;
}
