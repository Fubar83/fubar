namespace Fubar.Studio.Core.Models;

/// <summary>
/// Comparison options at ONE level of the hierarchy (global / folder / request), where every member is
/// nullable and <c>null</c> means "inherit whatever the level above decided".
///
/// That is the whole point of the nullability: a request that only wants to change
/// <see cref="IgnoreWhitespace"/> leaves everything else null and keeps inheriting it, rather than
/// silently pinning a copy of every other option the moment it overrides one. Combined with
/// <c>FubarJson</c>'s <c>WhenWritingNull</c>, an un-overridden setting is simply absent from the file -
/// so a <c>request.json</c> shows exactly what that request overrides and nothing else.
///
/// Deliberately NOT <c>Fubar.Diff.Core.Comparison.ComparisonOptions</c>: <c>Fubar.Studio.Core</c> does
/// not reference the diff projects at all (only <c>Fubar.Studio.UI</c> does), and the architecture
/// tests enforce that. <c>ComparisonSettingsMapper</c> in the UI layer is the one place the two meet.
/// </summary>
public sealed class ComparisonSettings
{
    /// <summary>Ignore leading/trailing whitespace when matching lines. Text comparison only.</summary>
    public bool? IgnoreWhitespace { get; set; }

    /// <summary>Ignore case when matching lines. Text comparison only.</summary>
    public bool? IgnoreCase { get; set; }

    /// <summary>Pretty-print JSON/XML for display in the Text view. Never rewrites the source.</summary>
    public bool? NormalizeStructure { get; set; }

    /// <summary>Report a JSON property that only moved. JSON comparison only.</summary>
    public bool? ReportPropertyOrder { get; set; }

    /// <summary>Match JSON array elements by index instead of by an identity key.</summary>
    public bool? MatchArraysByPosition { get; set; }

    /// <summary>Treat an explicit JSON <c>null</c> and an absent property as the same thing.</summary>
    public bool? IgnoreNullVsMissing { get; set; }

    /// <summary>
    /// JSON paths whose differences are never reported - <c>$.meta.requestId</c>, <c>$..timestamp</c>,
    /// <c>$.items[*].updatedAt</c>. See <c>Fubar.Diff.Core.Json.JsonPathPattern</c> for the syntax.
    ///
    /// REPLACES the inherited list rather than adding to it, exactly like every other setting here.
    /// Union semantics were considered and rejected: with them, reading a request's rules would not
    /// tell you what actually applies to it, and there would be no way to drop an inherited rule that
    /// is wrong for one endpoint. An empty (non-null) list is therefore a meaningful override - it says
    /// "ignore nothing here", not "inherit".
    /// </summary>
    public List<string>? IgnoredPaths { get; set; }

    /// <summary>
    /// Identity keys for specific arrays, by JSON path (e.g. <c>$.users</c> → <c>id</c>), overriding
    /// the auto-detection in <c>Fubar.Diff.Core.Json.ArrayKeyResolver</c>. Replaces, not merges - see
    /// <see cref="IgnoredPaths"/>.
    /// </summary>
    public Dictionary<string, string>? ArrayKeyOverrides { get; set; }

    /// <summary>
    /// True when this level overrides nothing at all, so a caller can drop the whole section rather
    /// than persisting an object full of nulls.
    /// </summary>
    public bool IsEmpty =>
        IgnoreWhitespace is null
        && IgnoreCase is null
        && NormalizeStructure is null
        && ReportPropertyOrder is null
        && MatchArraysByPosition is null
        && IgnoreNullVsMissing is null
        && IgnoredPaths is null
        && ArrayKeyOverrides is null;

    /// <summary>A detached copy, so editing a draft cannot mutate what is still on disk.</summary>
    public ComparisonSettings Clone() => new()
    {
        IgnoreWhitespace = IgnoreWhitespace,
        IgnoreCase = IgnoreCase,
        NormalizeStructure = NormalizeStructure,
        ReportPropertyOrder = ReportPropertyOrder,
        MatchArraysByPosition = MatchArraysByPosition,
        IgnoreNullVsMissing = IgnoreNullVsMissing,
        IgnoredPaths = IgnoredPaths is null ? null : [.. IgnoredPaths],
        ArrayKeyOverrides = ArrayKeyOverrides is null ? null : new Dictionary<string, string>(ArrayKeyOverrides),
    };
}
