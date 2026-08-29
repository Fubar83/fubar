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

    /// <summary>
    /// The root of the comparison-settings hierarchy: defaults every workspace, folder and request
    /// inherits unless it overrides them. Null (the common case) means "no global opinion - use the
    /// built-in defaults". See <c>ComparisonSettingsResolver</c>.
    ///
    /// Global rather than per-workspace on purpose: preferences like "ignore whitespace" describe how
    /// the USER likes to read a diff, not anything about a particular workspace's content - the
    /// content-specific rules (ignore paths, array keys) are the ones that belong further down.
    /// </summary>
    public ComparisonSettings? Comparison { get; set; }
}
