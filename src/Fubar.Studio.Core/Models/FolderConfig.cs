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
}
