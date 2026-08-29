using System.Collections.Generic;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Core.Settings;

/// <summary>
/// Everything that survives a restart.
///
/// A plain record with defaults on every member, so a settings file written by an older version - or
/// a hand-edited one missing half its properties - still loads. Losing a preference is a nuisance;
/// refusing to start because of one is not acceptable.
/// </summary>
public sealed record AppSettings
{
    /// <summary>What a first run gets.</summary>
    public static AppSettings Default { get; } = new();

    /// <summary>Dark / Light / System, as the name of the theme enum.</summary>
    public string Theme { get; init; } = "System";

    /// <summary>Recently compared file pairs, most recent first.</summary>
    public IReadOnlyList<RecentComparison> Recent { get; init; } = [];

    /// <summary>Comparison options, so the user's working preferences persist.</summary>
    public bool IgnoreWhitespace { get; init; }

    public bool IgnoreCase { get; init; }

    /// <summary>Pretty-print JSON/XML for display in the Text view - see the "Reformat" toggle.</summary>
    public bool NormalizeStructure { get; init; }

    /// <summary>Compare in Unicode normal form C - see <c>ComparisonOptions.NormalizeUnicode</c>.</summary>
    public bool NormalizeUnicode { get; init; }

    /// <summary>
    /// Reveal invisible characters in the panes. A display preference rather than a comparison one,
    /// persisted for the same reason the theme is: someone who wants to see NBSPs wants to see them
    /// every session, not to re-tick a box each time.
    /// </summary>
    public bool ShowInvisibles { get; init; }

    /// <summary>
    /// Hide long stretches of unchanged context behind a collapsed placeholder. On by default - see
    /// <c>DiffPaneViewModel.CollapseUnchanged</c> for why that default is the opposite way round from
    /// the "never change what the user is shown" rule that governs reformatting.
    /// </summary>
    public bool CollapseUnchanged { get; init; } = true;

    /// <summary>
    /// Regular expressions whose matches are ignored when comparing - a build timestamp, a generated
    /// GUID, a version stamp. See <c>LinePatternMask</c>.
    /// </summary>
    public IReadOnlyList<string> IgnoredLinePatterns { get; init; } = [];

    /// <summary>Treat comments as absent - see <c>CodeComparisonOptions.IgnoreComments</c>.</summary>
    public bool IgnoreComments { get; init; }

    /// <summary>Treat added or removed blank lines as noise - see <c>CodeComparisonOptions.IgnoreBlankLines</c>.</summary>
    public bool IgnoreBlankLines { get; init; }

    /// <summary>
    /// Colour the panes by the file's own grammar. On by default - it is the difference between
    /// reading a diff of code and reading a diff of text that happens to be code - and persisted like
    /// the theme, since it is a preference about how someone reads rather than about one comparison.
    /// </summary>
    public bool SyntaxHighlighting { get; init; } = true;

    public bool ReportPropertyOrder { get; init; }

    public bool MatchArraysByPosition { get; init; }

    /// <summary>Treat an explicit JSON <c>null</c> and an absent property as the same thing.</summary>
    public bool IgnoreNullVsMissing { get; init; }

    /// <summary>Text or semantic comparison.</summary>
    public ComparisonMode Mode { get; init; } = ComparisonMode.Auto;

    /// <summary>
    /// Identity keys for specific arrays, by JSON path - the override hook the auto-detection in
    /// <see cref="Json.ArrayKeyResolver"/> promises. Kept here because it is per-user configuration,
    /// not something to re-enter per comparison.
    /// </summary>
    public IReadOnlyDictionary<string, string> ArrayKeyOverrides { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// JSON paths whose differences are never reported, in <see cref="Json.JsonPathPattern"/> syntax -
    /// e.g. a <c>requestId</c> or <c>timestamp</c> field that changes on every call.
    /// </summary>
    public IReadOnlyList<string> IgnoredPaths { get; init; } = [];

    /// <summary>How many entries <see cref="Recent"/> keeps.</summary>
    public const int MaxRecent = 10;
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
