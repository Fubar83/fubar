using System.Text.Json.Serialization;

namespace Fubar.Studio.Core.Models;

/// <summary>
/// How the app looks. Separate from everything else because it is the one group that is purely about
/// the person rather than about their work.
/// </summary>
public sealed class AppearanceSettings
{
    /// <summary>"System" | "Dark" | "Light" - a plain string so Core need not depend on the UI's
    /// <c>AppTheme</c> enum.</summary>
    public string Theme { get; set; } = "System";
}

/// <summary>
/// Defaults for sending a request. Both were hard-coded constants, which meant a user on a slow
/// internal API had no way to stop every call timing out, and a user hitting a large export had no way
/// to raise the cap that refused to load it.
/// </summary>
public sealed class RequestSettings
{
    /// <summary>Seconds before a request with no timeout of its own gives up. A request's own
    /// <see cref="RequestModel.TimeoutSeconds"/> still wins.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// Largest response body read into memory, in megabytes.
    ///
    /// <para>Finite on purpose: unbounded reads took the application down on a large or hostile
    /// response. Adjustable because "large" depends on the API - 64 MB is generous for an API client
    /// and too small for someone pulling a data export.</para>
    /// </summary>
    public int MaxResponseMegabytes { get; set; } = 64;
}

/// <summary>
/// Execution history - what is kept, and how much of it.
///
/// <para>Worth being able to turn OFF, which was not possible: history keeps whole response bodies on
/// disk, and a login response contains a token. It is ignored by Git and that is the right default,
/// but someone working on a machine they do not control should be able to say "keep none of this"
/// without editing a policy file.</para>
/// </summary>
public sealed class HistorySettings
{
    /// <summary>Record executions at all. Off keeps nothing and writes nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Executions kept per request, oldest evicted first.</summary>
    public int MaxEntriesPerRequest { get; set; } = 200;

    /// <summary>
    /// Largest response body a history entry will carry, in kilobytes. Zero keeps the timing and status
    /// of every execution but no bodies at all - history without the thing that makes it comparable,
    /// which is a reasonable trade for someone who does not want response payloads on disk.
    /// </summary>
    public int MaxResponseBodyKilobytes { get; set; } = 256;
}

/// <summary>
/// What was open last time. Not a preference - nobody chooses these, they are a record of where the
/// user was - which is why they are grouped away from the settings a person actually sets.
/// </summary>
public sealed class SessionState
{
    /// <summary>Root directories of every workspace tab open at last exit.</summary>
    public List<string> OpenWorkspacePaths { get; set; } = [];

    /// <summary>Which of <see cref="OpenWorkspacePaths"/> was the active tab.</summary>
    public string? ActiveWorkspacePath { get; set; }

    /// <summary>Whether the sidebar's Environments group is unfolded. Folded on a first run.</summary>
    public bool EnvironmentsExpanded { get; set; }

    /// <summary>Whether the sidebar's Auth Profiles group is unfolded. Folded on a first run.</summary>
    public bool AuthProfilesExpanded { get; set; }
}

/// <summary>
/// Global user preferences, kept outside any workspace because they apply across all of them.
///
/// <para>Grouped rather than flat. The flat shape it replaced put a theme, a list of open folders and
/// the root of the comparison hierarchy at the same level, which is three different KINDS of thing:
/// how the app looks, where the user was, and how their work behaves. Grouping is also what stops the
/// next setting being added in the wrong place.</para>
/// </summary>
public sealed class AppSettings
{
    public AppearanceSettings Appearance { get; set; } = new();

    public RequestSettings Requests { get; set; } = new();

    public HistorySettings History { get; set; } = new();

    /// <summary>
    /// The root of the comparison-settings hierarchy: defaults every workspace, folder and request
    /// inherits unless it overrides them. Null (the common case) means "no global opinion".
    ///
    /// <para>Its own group already - every member is nullable so a level can override one setting and
    /// keep inheriting the rest. Global rather than per-workspace because "ignore whitespace"
    /// describes how the USER likes to read a diff; the content-specific rules belong further down.</para>
    /// </summary>
    public ComparisonSettings? Comparison { get; set; }

    public SessionState Session { get; set; } = new();

    // --- reading a file written before the grouping -------------------------------------------------
    //
    // Same shape as AppVariable.IsSecret: the setter accepts the old name, the getter returns null so
    // JsonIgnoreCondition.WhenWritingNull leaves it out of anything written from now on. Without these
    // the shape change would deserialize to defaults and silently reset every preference - which reads
    // as the app losing settings rather than as a migration, and is exactly the kind of quiet loss the
    // rest of this codebase has been closing.

    [JsonPropertyName("theme")]
    public string? LegacyTheme
    {
        get => null;
        set { if (value is not null) { Appearance.Theme = value; } }
    }

    [JsonPropertyName("openWorkspacePaths")]
    public List<string>? LegacyOpenWorkspacePaths
    {
        get => null;
        set { if (value is not null) { Session.OpenWorkspacePaths = value; } }
    }

    [JsonPropertyName("activeWorkspacePath")]
    public string? LegacyActiveWorkspacePath
    {
        get => null;
        set { if (value is not null) { Session.ActiveWorkspacePath = value; } }
    }
}
