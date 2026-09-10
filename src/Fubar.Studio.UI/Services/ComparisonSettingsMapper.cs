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
public static class ComparisonSettingsMapper
{
    /// <summary>
    /// Builds the engine options for a comparison. <see cref="ComparisonMode.Auto"/> unless a reader
    /// asked for something else, so anything that parses as JSON - which most of what API Studio
    /// compares does - is compared semantically; the resolved settings decide only how strict that
    /// comparison is.
    /// </summary>
    /// <param name="mode">
    /// How to read this comparison. A parameter rather than a member of
    /// <c>ResolvedComparisonSettings</c> because it is not a property of the REQUEST: the settings say
    /// what counts as a difference for everyone who clones the repository, while this says how the
    /// person at the window wants to look at one response, once.
    /// </param>
    public static ComparisonOptions ToOptions(
        ResolvedComparisonSettings resolved,
        ComparisonMode mode = ComparisonMode.Auto) => new()
    {
        Mode = mode,
        IgnoreWhitespace = resolved.IgnoreWhitespace.Value,
        IgnoreCase = resolved.IgnoreCase.Value,
        NormalizeStructure = resolved.NormalizeStructure.Value,
        Json = new JsonComparisonOptions
        {
            ReportPropertyOrder = resolved.ReportPropertyOrder.Value,
            MatchArraysByPosition = resolved.MatchArraysByPosition.Value,
            IgnoreNullVsMissing = resolved.IgnoreNullVsMissing.Value,
            IgnoredPaths = [.. resolved.IgnoredPathValues],
            ArrayKeyOverrides = new Dictionary<string, string>(resolved.ArrayKeyOverrides.Value),
            UnorderedArrays = [.. resolved.UnorderedArrays.Value],
            PositionalArrays = [.. resolved.PositionalArrays.Value],
        },
    };
}
