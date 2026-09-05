using System.Collections.Generic;
using System.Text.Json.Serialization;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Core.Settings;

/// <summary>How the app looks and reads. About the person, not about any file.</summary>
public sealed record AppearanceSettings
{
    /// <summary>Dark / Light / System, as the name of the theme enum.</summary>
    public string Theme { get; init; } = "System";

    /// <summary>
    /// Colour the panes by the file's own grammar. On by default - the difference between reading a
    /// diff of code and a diff of text that happens to be code.
    /// </summary>
    public bool SyntaxHighlighting { get; init; } = true;

    /// <summary>Reveal invisible characters in the panes.</summary>
    public bool ShowInvisibles { get; init; }

    /// <summary>
    /// Hide long stretches of unchanged context behind a collapsed placeholder. On by default - see
    /// <c>DiffPaneViewModel.CollapseUnchanged</c> for why that default is the opposite way round from
    /// the "never change what the user is shown" rule that governs reformatting.
    /// </summary>
    public bool CollapseUnchanged { get; init; } = true;

    /// <summary>
    /// Wrap long lines in the unified view. Unified-only - see <c>DiffPaneViewModel.WordWrap</c> for
    /// why the side-by-side panes cannot have it.
    /// </summary>
    public bool WordWrap { get; init; }

    /// <summary>Pretty-print JSON/XML for display in the Text view - the "Reformat" toggle.</summary>
    public bool NormalizeStructure { get; init; }
}

/// <summary>
/// What counts as a difference: the user's own defaults.
///
/// <para>These describe how the READER wants to work. The same options exist per-file in
/// <see cref="ProjectRule"/>, which is a different thing and deliberately so - <c>.fubardiff.json</c>
/// says "this repository's snapshots have a requestId that changes every run", a fact true for the
/// whole team. A project rule ADDS to what is here rather than replacing it.</para>
/// </summary>
public sealed record ComparisonDefaults
{
    /// <summary>Text or semantic comparison.</summary>
    public ComparisonMode Mode { get; init; } = ComparisonMode.Auto;

    public bool IgnoreWhitespace { get; init; }

    public bool IgnoreCase { get; init; }

    /// <summary>Compare in Unicode normal form C - see <c>ComparisonOptions.NormalizeUnicode</c>.</summary>
    public bool NormalizeUnicode { get; init; }

    /// <summary>Treat comments as absent - see <c>CodeComparisonOptions.IgnoreComments</c>.</summary>
    public bool IgnoreComments { get; init; }

    /// <summary>Treat added or removed blank lines as noise.</summary>
    public bool IgnoreBlankLines { get; init; }

    /// <summary>
    /// Work out what changed member by member for source code. On by default, unlike the two rules
    /// above, because it changes nothing about the comparison itself - it adds an answer beside it.
    /// </summary>
    public bool CodeStructure { get; init; } = true;

    /// <summary>
    /// Regular expressions whose matches are ignored - a build timestamp, a generated GUID, a version
    /// stamp. See <c>LinePatternMask</c>.
    /// </summary>
    public IReadOnlyList<string> IgnoredLinePatterns { get; init; } = [];
}

/// <summary>Comparison options that only mean anything for JSON and YAML.</summary>
public sealed record JsonComparisonDefaults
{
    public bool ReportPropertyOrder { get; init; }

    public bool MatchArraysByPosition { get; init; }

    /// <summary>Treat an explicit JSON <c>null</c> and an absent property as the same thing.</summary>
    public bool IgnoreNullVsMissing { get; init; }

    /// <summary>Identity keys for specific arrays, by JSON path - the override hook the auto-detection
    /// in <see cref="Json.ArrayKeyResolver"/> promises.</summary>
    public IReadOnlyDictionary<string, string> ArrayKeyOverrides { get; init; } =
        new Dictionary<string, string>();

    /// <summary>JSON paths whose differences are never reported, in <see cref="Json.JsonPathPattern"/>
    /// syntax - a <c>requestId</c> or <c>timestamp</c> that changes on every call.</summary>
    public IReadOnlyList<string> IgnoredPaths { get; init; } = [];
}

/// <summary>
/// How the Json view lays a document out when the Pretty button is on.
///
/// <para>Display only. None of it changes what the comparison found, and none of it touches a file -
/// which is why it is a group of its own rather than sitting among the options that do.</para>
/// </summary>
public sealed record JsonFormattingSettings
{
    public int IndentSize { get; init; } = 2;

    public bool UseTabs { get; init; }

    public bool InlineSimpleContainers { get; init; } = true;

    public bool SortProperties { get; init; }
}

/// <summary>Folder comparison. Its own group because none of it means anything for a file pair.</summary>
public sealed record FolderSettings
{
    /// <summary>Show identical files. Off by default - see <c>FolderViewModel.ShowIdentical</c> for why
    /// that default is what makes the feature usable.</summary>
    public bool ShowIdentical { get; init; }

    /// <summary>Names a folder comparison never descends into. Empty means "use the defaults", which is
    /// how a settings file written before this existed keeps working.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>Pair files within ONE folder by name, rather than comparing two folders.</summary>
    public bool LinkedMode { get; init; }

    /// <summary>The name markers that pair two files in one folder, each written <c>left = right</c>.
    /// Empty keeps the built-in conventions - see <c>LinkRule.Defaults</c>.</summary>
    public IReadOnlyList<string> LinkRules { get; init; } = [];
}

/// <summary>
/// Live behaviour - when the comparison re-runs on its own.
/// </summary>
public sealed record RefreshSettings
{
    /// <summary>Re-run a comparison when its files change on disk.</summary>
    public bool AutoRefresh { get; init; } = true;

    /// <summary>Re-run as an editable pane is typed into, shortly after typing stops. With it off the
    /// diff waits for F5.</summary>
    public bool LiveDiff { get; init; } = true;
}

/// <summary>
/// Everything that survives a restart.
///
/// <para>Grouped rather than flat. The flat shape it replaced held thirty-one properties at one level
/// - a theme, a JSON indent width, a folder exclude list and the comparison mode all as peers - so
/// adding one meant touching the record, <c>ApplyDefaults</c>, <c>CaptureOptions</c> and the settings
/// window, and forgetting any of those left a setting that looked complete and did nothing. That is
/// not hypothetical: CLAUDE.md records exactly that happening to <c>IgnoredPaths</c>,
/// <c>IgnoreNullVsMissing</c> and <c>ArrayKeyOverrides</c>, which were built in Core with persistence
/// fields waiting while the view model never read them.</para>
///
/// <para>Defaults on every member, so a file written by an older version - or hand-edited and missing
/// half its properties - still loads. Losing a preference is a nuisance; refusing to start over one is
/// not acceptable.</para>
/// </summary>
public sealed record AppSettings
{
    /// <summary>What a first run gets.</summary>
    public static AppSettings Default { get; } = new();

    public AppearanceSettings Appearance { get; init; } = new();

    public ComparisonDefaults Comparison { get; init; } = new();

    public JsonComparisonDefaults Json { get; init; } = new();

    public JsonFormattingSettings JsonFormatting { get; init; } = new();

    public FolderSettings Folders { get; init; } = new();

    public RefreshSettings Refresh { get; init; } = new();

    /// <summary>
    /// Recently compared file pairs, most recent first. Not a preference - nobody chooses it - which is
    /// why it sits apart from the settings a person actually sets.
    /// </summary>
    public IReadOnlyList<RecentComparison> Recent { get; init; } = [];

    /// <summary>How many entries <see cref="Recent"/> keeps.</summary>
    public const int MaxRecent = 10;

    // --- reading a file written before the grouping -------------------------------------------------
    //
    // Each legacy setter folds the old flat property into its group; each getter returns null so
    // JsonIgnoreCondition.WhenWritingNull keeps them out of anything written from now on. Without them
    // the shape change would silently reset every preference, which reads as the app losing settings
    // rather than as a migration.
    //
    // `Editing` is deliberately NOT among them. It persisted whether the panes open typed-into, and a
    // reading tool should not reopen with a caret in someone's source file - see
    // ComparisonViewModel.IsEditing, which already argues that and defaults it off. Persisting it meant
    // one accidental toggle was permanent. It is still a per-session toggle; it just no longer outlives
    // the session.

    [JsonPropertyName("theme")]
    public string? LegacyTheme { get => null; init { if (value is not null) { Appearance = Appearance with { Theme = value }; } } }

    [JsonPropertyName("syntaxHighlighting")]
    public bool? LegacySyntaxHighlighting { get => null; init { if (value is { } v) { Appearance = Appearance with { SyntaxHighlighting = v }; } } }

    [JsonPropertyName("showInvisibles")]
    public bool? LegacyShowInvisibles { get => null; init { if (value is { } v) { Appearance = Appearance with { ShowInvisibles = v }; } } }

    [JsonPropertyName("collapseUnchanged")]
    public bool? LegacyCollapseUnchanged { get => null; init { if (value is { } v) { Appearance = Appearance with { CollapseUnchanged = v }; } } }

    [JsonPropertyName("wordWrap")]
    public bool? LegacyWordWrap { get => null; init { if (value is { } v) { Appearance = Appearance with { WordWrap = v }; } } }

    [JsonPropertyName("normalizeStructure")]
    public bool? LegacyNormalizeStructure { get => null; init { if (value is { } v) { Appearance = Appearance with { NormalizeStructure = v }; } } }

    [JsonPropertyName("mode")]
    public ComparisonMode? LegacyMode { get => null; init { if (value is { } v) { Comparison = Comparison with { Mode = v }; } } }

    [JsonPropertyName("ignoreWhitespace")]
    public bool? LegacyIgnoreWhitespace { get => null; init { if (value is { } v) { Comparison = Comparison with { IgnoreWhitespace = v }; } } }

    [JsonPropertyName("ignoreCase")]
    public bool? LegacyIgnoreCase { get => null; init { if (value is { } v) { Comparison = Comparison with { IgnoreCase = v }; } } }

    [JsonPropertyName("normalizeUnicode")]
    public bool? LegacyNormalizeUnicode { get => null; init { if (value is { } v) { Comparison = Comparison with { NormalizeUnicode = v }; } } }

    [JsonPropertyName("ignoreComments")]
    public bool? LegacyIgnoreComments { get => null; init { if (value is { } v) { Comparison = Comparison with { IgnoreComments = v }; } } }

    [JsonPropertyName("ignoreBlankLines")]
    public bool? LegacyIgnoreBlankLines { get => null; init { if (value is { } v) { Comparison = Comparison with { IgnoreBlankLines = v }; } } }

    [JsonPropertyName("codeStructure")]
    public bool? LegacyCodeStructure { get => null; init { if (value is { } v) { Comparison = Comparison with { CodeStructure = v }; } } }

    [JsonPropertyName("ignoredLinePatterns")]
    public IReadOnlyList<string>? LegacyIgnoredLinePatterns { get => null; init { if (value is not null) { Comparison = Comparison with { IgnoredLinePatterns = value }; } } }

    [JsonPropertyName("reportPropertyOrder")]
    public bool? LegacyReportPropertyOrder { get => null; init { if (value is { } v) { Json = Json with { ReportPropertyOrder = v }; } } }

    [JsonPropertyName("matchArraysByPosition")]
    public bool? LegacyMatchArraysByPosition { get => null; init { if (value is { } v) { Json = Json with { MatchArraysByPosition = v }; } } }

    [JsonPropertyName("ignoreNullVsMissing")]
    public bool? LegacyIgnoreNullVsMissing { get => null; init { if (value is { } v) { Json = Json with { IgnoreNullVsMissing = v }; } } }

    [JsonPropertyName("arrayKeyOverrides")]
    public IReadOnlyDictionary<string, string>? LegacyArrayKeyOverrides { get => null; init { if (value is not null) { Json = Json with { ArrayKeyOverrides = value }; } } }

    [JsonPropertyName("ignoredPaths")]
    public IReadOnlyList<string>? LegacyIgnoredPaths { get => null; init { if (value is not null) { Json = Json with { IgnoredPaths = value }; } } }

    [JsonPropertyName("jsonIndentSize")]
    public int? LegacyJsonIndentSize { get => null; init { if (value is { } v) { JsonFormatting = JsonFormatting with { IndentSize = v }; } } }

    [JsonPropertyName("jsonUseTabs")]
    public bool? LegacyJsonUseTabs { get => null; init { if (value is { } v) { JsonFormatting = JsonFormatting with { UseTabs = v }; } } }

    [JsonPropertyName("jsonInlineSimpleContainers")]
    public bool? LegacyJsonInlineSimpleContainers { get => null; init { if (value is { } v) { JsonFormatting = JsonFormatting with { InlineSimpleContainers = v }; } } }

    [JsonPropertyName("jsonSortProperties")]
    public bool? LegacyJsonSortProperties { get => null; init { if (value is { } v) { JsonFormatting = JsonFormatting with { SortProperties = v }; } } }

    [JsonPropertyName("autoRefresh")]
    public bool? LegacyAutoRefresh { get => null; init { if (value is { } v) { Refresh = Refresh with { AutoRefresh = v }; } } }

    [JsonPropertyName("liveDiff")]
    public bool? LegacyLiveDiff { get => null; init { if (value is { } v) { Refresh = Refresh with { LiveDiff = v }; } } }

    [JsonPropertyName("folderShowIdentical")]
    public bool? LegacyFolderShowIdentical { get => null; init { if (value is { } v) { Folders = Folders with { ShowIdentical = v }; } } }

    [JsonPropertyName("folderExclude")]
    public IReadOnlyList<string>? LegacyFolderExclude { get => null; init { if (value is not null) { Folders = Folders with { Exclude = value }; } } }

    [JsonPropertyName("folderLinkedMode")]
    public bool? LegacyFolderLinkedMode { get => null; init { if (value is { } v) { Folders = Folders with { LinkedMode = v }; } } }

    [JsonPropertyName("folderLinkRules")]
    public IReadOnlyList<string>? LegacyFolderLinkRules { get => null; init { if (value is not null) { Folders = Folders with { LinkRules = value }; } } }
}

/// <summary>One remembered comparison.</summary>
/// <param name="Left">Left-hand path.</param>
/// <param name="Right">Right-hand path.</param>
public sealed record RecentComparison(string Left, string Right)
{
    /// <summary>
    /// A short label for a menu, e.g. <c>old.json ↔ new.json</c>.
    ///
    /// Not persisted: it is derived from the two paths, so writing it would bloat the settings file
    /// and invite someone hand-editing it to change a value that is ignored on load.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName =>
        $"{System.IO.Path.GetFileName(Left)} ↔ {System.IO.Path.GetFileName(Right)}";
}
