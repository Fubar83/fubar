using System.Text.Json.Serialization;

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
    /// ADDS to and REMOVES from the inherited list rather than replacing it - see
    /// <see cref="InheritedPaths"/>, which carries the reasoning and the compatibility rule for files
    /// written in the old shape. Null still means "this level says nothing".
    /// </summary>
    public InheritedPaths? IgnoredPaths { get; set; }

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
    /// <remarks>
    /// An <see cref="IgnoredPaths"/> that adds and removes nothing counts as nothing, not as
    /// something. Editing is what makes the difference: adding a path and taking it away again leaves
    /// the object behind, and treating that as an override wrote
    /// <c>"comparison": { "ignoredPaths": { "add": [], "remove": [] } }</c> into a file that overrides
    /// nothing - which then reads, to the next person, as a level that deliberately said something.
    /// </remarks>
    [JsonIgnore]
    public bool IsEmpty =>
        IgnoreWhitespace is null
        && IgnoreCase is null
        && NormalizeStructure is null
        && ReportPropertyOrder is null
        && MatchArraysByPosition is null
        && IgnoreNullVsMissing is null
        && IgnoredPaths is null or { IsEmpty: true }
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
        IgnoredPaths = IgnoredPaths?.Clone(),
        ArrayKeyOverrides = ArrayKeyOverrides is null ? null : new Dictionary<string, string>(ArrayKeyOverrides),
    };
}
