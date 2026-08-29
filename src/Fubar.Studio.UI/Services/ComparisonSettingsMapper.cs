using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Studio.Core.Comparison;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Translates Studio's resolved comparison settings into the diff engine's own options.
///
/// This is deliberately the ONLY place the two vocabularies meet. <c>Fubar.Studio.Core</c> does not
/// reference the diff projects at all - the architecture tests enforce it - so its
/// <c>ComparisonSettings</c> is a parallel, nullable-per-member shape rather than a reuse of
/// <see cref="ComparisonOptions"/>. Keeping the mapping in one function is what stops that duplication
/// from drifting: add a setting to one side and this stops compiling until the other side has it too.
/// </summary>
internal static class ComparisonSettingsMapper
{
    /// <summary>
    /// Builds the engine options for a comparison. <see cref="ComparisonMode.Auto"/> always, so
    /// anything that parses as JSON - which most of what API Studio compares does - is compared
    /// semantically; the resolved settings decide only how strict that comparison is.
    /// </summary>
    public static ComparisonOptions ToOptions(ResolvedComparisonSettings resolved) => new()
    {
        Mode = ComparisonMode.Auto,
        IgnoreWhitespace = resolved.IgnoreWhitespace.Value,
        IgnoreCase = resolved.IgnoreCase.Value,
        NormalizeStructure = resolved.NormalizeStructure.Value,
        Json = new JsonComparisonOptions
        {
            ReportPropertyOrder = resolved.ReportPropertyOrder.Value,
            MatchArraysByPosition = resolved.MatchArraysByPosition.Value,
            IgnoreNullVsMissing = resolved.IgnoreNullVsMissing.Value,
            IgnoredPaths = [.. resolved.IgnoredPaths.Value],
            ArrayKeyOverrides = new Dictionary<string, string>(resolved.ArrayKeyOverrides.Value),
        },
    };
}
