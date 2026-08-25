namespace Fubar.Studio.Core.Models;

/// <summary>
/// The root <c>fubar.json</c> document: public workspace variables and settings, committed to Git.
/// Matches the (placeholder) schema at https://fubarhttp.dev/schemas/v1/app.schema.json.
/// </summary>
public sealed class AppManifest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public required string Name { get; set; }

    public string Version { get; set; } = "1.0.0";

    public List<AppVariable> Variables { get; set; } = [];

    /// <summary>Id of the last-active <see cref="WorkspaceEnvironment"/> for this workspace, remembered
    /// across sessions - see the Left Pane header's environment selector (LeftPane.md §4.1).</summary>
    public string? ActiveEnvironmentId { get; set; }
}
