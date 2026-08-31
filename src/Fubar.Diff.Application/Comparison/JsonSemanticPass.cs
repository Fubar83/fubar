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
    private readonly IJsonParser _parser;

    public JsonSemanticPass(IJsonParser parser) => _parser = parser;

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
        ComparisonOptions options)
    {
        if (options.Mode == ComparisonMode.Text)
        {
            return JsonSemanticOutcome.NotAttempted(textResult);
        }

        if (!_parser.TryParse(leftText, out var left, out var leftError) || left is null)
        {
            return Skipped(textResult, options, "left", leftError);
        }

        if (!_parser.TryParse(rightText, out var right, out var rightError) || right is null)
        {
            return Skipped(textResult, options, "right", rightError);
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
    public IReadOnlyList<JsonChange>? TryCompareOriginalText(string leftText, string rightText, ComparisonOptions options)
    {
        if (options.Mode == ComparisonMode.Text)
        {
            return null;
        }

        if (!_parser.TryParse(leftText, out var left, out _) || left is null)
        {
            return null;
        }

        if (!_parser.TryParse(rightText, out var right, out _) || right is null)
        {
            return null;
        }

        return JsonSemanticDiffer.Compare(left, right, options.Json);
    }

    /// <summary>
    /// Re-lays-out a document for reading, or returns null when it does not parse.
    ///
    /// Null rather than the original text, so the caller can tell "nothing to do" from "this is not
    /// JSON" - the second means the pretty button should not have been offered at all.
    /// </summary>
    public string? TryFormat(string text, JsonFormatOptions format) =>
        _parser.TryParse(text, out var root, out _) && root is not null
            ? JsonFormatter.Format(root, format)
            : null;

    /// <summary>
    /// Falls back to the text result. In <see cref="ComparisonMode.Auto"/> this is unremarkable - most
    /// files are not JSON - so no reason is reported and the UI stays quiet. When the user explicitly
    /// asked for JSON, the parse error is worth surfacing.
    /// </summary>
    private static JsonSemanticOutcome Skipped(
        DiffResult textResult,
        ComparisonOptions options,
        string side,
        JsonParseException? error) =>
        options.Mode == ComparisonMode.Json
            ? JsonSemanticOutcome.NotAttempted(textResult) with
            {
                FallbackReason = $"The {side}-hand file is not valid JSON, so the files were compared as text. "
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
