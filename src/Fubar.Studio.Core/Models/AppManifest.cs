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

    /// <summary>
    /// Which shape <c>collections/</c> is in. Absent means <see cref="WorkspaceFormat.Requests"/>,
    /// which is what every workspace written before endpoints existed is - so an old workspace opens
    /// exactly as it did, and is never converted behind its owner's back.
    /// </summary>
    public WorkspaceFormat Format { get; set; } = WorkspaceFormat.Requests;

    public List<AppVariable> Variables { get; set; } = [];

    /// <summary>Id of the last-active <see cref="WorkspaceEnvironment"/> for this workspace, remembered
    /// across sessions - see the Left Pane header's environment selector (LeftPane.md §4.1).</summary>
    public string? ActiveEnvironmentId { get; set; }
}
