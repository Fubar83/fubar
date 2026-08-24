using System.Collections.Generic;
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
        var (significantLeft, significantRight) = JsonChangeLines.Collect(changes);

        return new JsonSemanticOutcome(
            SemanticLineFilter.Apply(textResult, significantLeft, significantRight),
            Applied: true,
            Changes: changes,
            FallbackReason: null);
    }

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
