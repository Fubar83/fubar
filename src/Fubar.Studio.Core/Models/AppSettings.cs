namespace Fubar.Studio.Core.Models;

/// <summary>
/// Fubar's global (not per-workspace) user preferences - theme choice plus which workspaces were
/// open last session - persisted outside any workspace directory since they apply across every
/// workspace the user opens. See <c>IAppSettingsService</c>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>"System" | "Dark" | "Light" - kept as a plain string so Fubar.Studio.Core doesn't need to
    /// depend on Fubar.Studio.UI's <c>AppTheme</c> enum.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Root directories of every workspace tab open at last exit, so the app can restore
    /// them all on the next launch.</summary>
    public List<string> OpenWorkspacePaths { get; set; } = [];

    /// <summary>Which of <see cref="OpenWorkspacePaths"/> was the active tab, if any.</summary>
    public string? ActiveWorkspacePath { get; set; }
}
