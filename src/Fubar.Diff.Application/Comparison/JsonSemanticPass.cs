using System.Collections.Generic;
using System.Linq;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// Refines a text alignment with a semantic JSON comparison, when both documents parse.
///
/// Deliberately a REFINEMENT rather than a separate pipeline. Producing an alignment from the AST
/// would mean reimplementing filler rows, hunk grouping and ordering, and would give the two modes
/// subtly different behaviour. Instead the text pass decides how lines line up and this decides which
/// of them matter - so every renderer, the diff map, navigation and merge work unchanged in both modes.
/// </summary>
public sealed class JsonSemanticPass
{
    private readonly IJsonParser _json;
    private readonly IYamlParser? _yaml;

    /// <summary>
    /// The YAML parser is optional so a host that never compares YAML - or a test that does not care -
    /// need not supply one. Where it is missing, a YAML file simply falls back to a text comparison,
    /// which is what happens for any unparseable file anyway.
    /// </summary>
    public JsonSemanticPass(IJsonParser json, IYamlParser? yaml = null)
    {
        _json = json;
        _yaml = yaml;
    }

    /// <summary>
    /// Parses one side in whichever format it was decided to be - see <see cref="StructuredFormatDetector"/>.
    /// </summary>
    private bool TryParse(string text, StructuredFormat format, out JsonAstNode? node, out JsonParseException? error)
    {
        node = null;
        error = null;

        return format switch
        {
            StructuredFormat.Json => _json.TryParse(text, out node, out error),
            StructuredFormat.Yaml => _yaml is not null && _yaml.TryParse(text, out node, out error),
            _ => false,
        };
    }

    /// <summary>
    /// Applies the semantic pass, or explains why it did not run.
    /// </summary>
    /// <param name="textResult">The alignment from the text pass.</param>
    /// <param name="leftText">The left document's full text, as displayed.</param>
    /// <param name="rightText">The right document's full text.</param>
    /// <param name="options">Comparison options, including the mode.</param>
    public JsonSemanticOutcome Apply(
        DiffResult textResult,
        string leftText,
        string rightText,
        ComparisonOptions options,
        StructuredFormat leftFormat = StructuredFormat.Json,
        StructuredFormat rightFormat = StructuredFormat.Json)
    {
        if (options.Mode == ComparisonMode.Text
            || leftFormat == StructuredFormat.None
            || rightFormat == StructuredFormat.None)
        {
            return JsonSemanticOutcome.NotAttempted(textResult);
        }

        if (!TryParse(leftText, leftFormat, out var left, out var leftError) || left is null)
        {
            return Skipped(textResult, options, "left", leftFormat, leftError);
        }

        if (!TryParse(rightText, rightFormat, out var right, out var rightError) || right is null)
        {
            return Skipped(textResult, options, "right", rightFormat, rightError);
        }

        var changes = JsonSemanticDiffer.Compare(left, right, options.Json);

        // Split, not filtered: the significant lines decide what is a change, the ignored ones decide
        // where to draw a faint band. Collecting them together would make an ignored field count as a
        // real difference again.
        var (significantLeft, significantRight) =
            JsonChangeLines.Collect([.. changes.Where(c => !c.IsIgnored)]);
        var (ignoredLeft, ignoredRight) =
            JsonChangeLines.Collect([.. changes.Where(c => c.IsIgnored)]);

        return new JsonSemanticOutcome(
            SemanticLineFilter.Apply(textResult, significantLeft, significantRight, ignoredLeft, ignoredRight),
            Applied: true,
            Changes: changes,
            FallbackReason: null);
    }

    /// <summary>
    /// Re-runs the comparison against text that was never canonicalized for alignment - the Json view
    /// shows each side exactly as it was given, not reformatted, so it needs <see cref="JsonChange"/>
    /// spans that address THAT text rather than the pretty-printed copy <see cref="Apply"/> works from.
    ///
    /// A second full diff rather than re-resolving the existing changes into a differently-formatted
    /// tree: canonicalisation preserves property and array order exactly, so parsing and diffing the
    /// original text produces the identical change list in the identical order - same paths, same
    /// kinds, same count - differing only in which text each span points into. That guarantee is what
    /// lets a caller pair this list up with <see cref="Apply"/>'s by position. Re-running the (already
    /// trusted) differ is simpler and safer than writing a second way to map a path onto a fresh AST.
    ///
    /// Returns null wherever <see cref="Apply"/> would not have produced a semantic result either -
    /// Text mode, or either side failing to parse - so a caller can tell "did not run" apart from
    /// "ran and found nothing", exactly as with <see cref="JsonSemanticOutcome.Applied"/>.
    /// </summary>
    public IReadOnlyList<JsonChange>? TryCompareOriginalText(
        string leftText,
        string rightText,
        ComparisonOptions options,
        StructuredFormat leftFormat = StructuredFormat.Json,
        StructuredFormat rightFormat = StructuredFormat.Json)
    {
        if (options.Mode == ComparisonMode.Text)
        {
            return null;
        }

        if (!TryParse(leftText, leftFormat, out var left, out _) || left is null)
        {
            return null;
        }

        if (!TryParse(rightText, rightFormat, out var right, out _) || right is null)
        {
            return null;
        }

        return JsonSemanticDiffer.Compare(left, right, options.Json);
    }

    /// <summary>
    /// What each array in the pair could be matched by, so the change tree can offer a real choice on
    /// a right-click rather than a text box the user fills from memory. Empty when either side does
    /// not parse.
    /// </summary>
    public IReadOnlyDictionary<string, ArrayKeyChoices> ScanArrays(
        string leftText,
        string rightText,
        ComparisonOptions options,
        StructuredFormat leftFormat = StructuredFormat.Json,
        StructuredFormat rightFormat = StructuredFormat.Json)
    {
        if (options.Mode == ComparisonMode.Text
            || !TryParse(leftText, leftFormat, out var left, out _)
            || !TryParse(rightText, rightFormat, out var right, out _))
        {
            return new Dictionary<string, ArrayKeyChoices>();
        }

        return ArrayKeyScanner.Scan(left, right, options.Json);
    }

    /// <summary>
    /// Re-lays-out a document for reading, or returns null when it does not parse.
    ///
    /// Null rather than the original text, so the caller can tell "nothing to do" from "this is not
    /// JSON" - the second means the pretty button should not have been offered at all.
    /// </summary>
    public string? TryFormat(string text, JsonFormatOptions format) =>
        _json.TryParse(text, out var root, out _) && root is not null
            ? JsonFormatter.Format(root, format)
            : null;

    /// <summary>
    /// Falls back to the text result. In <see cref="ComparisonMode.Auto"/> this is unremarkable - most
    /// files are not JSON - so no reason is reported and the UI stays quiet. When the user explicitly
    /// asked for JSON, the parse error is worth surfacing.
    /// </summary>
    /// <summary>
    /// Says why nothing structural happened, but only when the user ASKED for a structural comparison.
    /// In Auto, a file that does not parse is the ordinary case - most files are not JSON - and
    /// announcing it every time would be noise.
    /// </summary>
    private static JsonSemanticOutcome Skipped(
        DiffResult textResult,
        ComparisonOptions options,
        string side,
        StructuredFormat format,
        JsonParseException? error) =>
        options.Mode is ComparisonMode.Json or ComparisonMode.Yaml
            ? JsonSemanticOutcome.NotAttempted(textResult) with
            {
                FallbackReason = $"The {side}-hand file is not valid {(format == StructuredFormat.Yaml ? "YAML" : "JSON")}, "
                                 + "so the files were compared as text. "
                                 + (error?.Message ?? string.Empty),
            }
            : JsonSemanticOutcome.NotAttempted(textResult);
}

/// <summary>The result of attempting a semantic pass.</summary>
/// <param name="Result">The diff to display - refined if the pass ran, the text result otherwise.</param>
/// <param name="Applied">Whether the semantic comparison actually ran.</param>
/// <param name="Changes">The semantic changes, for the tree view. Empty when the pass did not run.</param>
/// <param name="FallbackReason">Why it did not run, when that is worth telling the user.</param>
public sealed record JsonSemanticOutcome(
    DiffResult Result,
    bool Applied,
    IReadOnlyList<JsonChange> Changes,
    string? FallbackReason)
{
    public static JsonSemanticOutcome NotAttempted(DiffResult result) =>
        new(result, Applied: false, Changes: [], FallbackReason: null);
}
