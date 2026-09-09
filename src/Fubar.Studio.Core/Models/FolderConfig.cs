namespace Fubar.Studio.Core.Models;

/// <summary>
/// Per-folder settings stored as <c>_folder.json</c> inside a collections subfolder: headers that
/// cascade down to every request beneath it, and an auth profile the folder assigns (itself
/// overridable by a deeper folder or a request). See <c>IWorkspaceService.GetInheritanceChainAsync</c>
/// for how a request's final combined headers/auth are computed (RequestEditorPane.md §5).
/// </summary>
public sealed class FolderConfig
{
    public List<KeyValueItem> Headers { get; set; } = [];

    public string? AuthProfileId { get; set; }

    /// <summary>
    /// Comparison options for every request beneath this folder, unless one of them overrides a
    /// setting itself. Null means this folder has no opinion and inherits whatever is above it.
    ///
    /// This is the "per project" level: a folder of endpoints from the same service usually shares the
    /// same noise (a <c>traceId</c> on every response, the same array identity key), and putting the
    /// rule here rather than on each request is the difference between writing it once and writing it
    /// twenty times. Cascades exactly like <see cref="Headers"/> - see
    /// <c>IInheritanceResolver.GetInheritanceChainAsync</c>, where a closer folder wins.
    /// </summary>
    public ComparisonSettings? Comparison { get; set; }

    /// <summary>What to redact and normalise on the way into a snapshot, and which headers to keep.
    /// Inherited exactly like <see cref="Comparison"/>; null means this level says nothing.</summary>
    public Snapshots.SnapshotPolicy? Snapshot { get; set; }

    /// <summary>Fields allowed to move, and by how much. Resolved per path, closest level winning -
    /// see <c>ToleranceResolver</c>. Null means this level states none.</summary>
    public List<Comparison.Tolerance>? Tolerances { get; set; }
}
