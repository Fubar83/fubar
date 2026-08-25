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

    public bool ReportPropertyOrder { get; init; }

    public bool MatchArraysByPosition { get; init; }

    /// <summary>Text or semantic comparison.</summary>
    public ComparisonMode Mode { get; init; } = ComparisonMode.Auto;

    /// <summary>
    /// Identity keys for specific arrays, by JSON path - the override hook the auto-detection in
    /// <see cref="Json.ArrayKeyResolver"/> promises. Kept here because it is per-user configuration,
    /// not something to re-enter per comparison.
    /// </summary>
    public IReadOnlyDictionary<string, string> ArrayKeyOverrides { get; init; } =
        new Dictionary<string, string>();

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
