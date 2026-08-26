using System.Collections.Generic;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// A completed comparison: both loaded documents, the options used, and the resulting diff. Keeping
/// the documents alongside the result is what lets the options be changed without touching the disk
/// again (see <see cref="IFileComparisonService.Recompare"/>).
/// </summary>
/// <param name="Left">The left-hand document.</param>
/// <param name="Right">The right-hand document.</param>
/// <param name="Options">The options this result was produced under.</param>
/// <param name="Result">The aligned diff.</param>
public sealed record FileComparison(
    TextDocument Left,
    TextDocument Right,
    ComparisonOptions Options,
    DiffResult Result)
{
    /// <summary>Whether the semantic JSON pass ran, as opposed to a plain text comparison.</summary>
    public bool IsSemantic { get; init; }

    /// <summary>
    /// The semantic changes, for the JSON tree view. Empty for a text comparison.
    /// </summary>
    public IReadOnlyList<JsonChange> SemanticChanges { get; init; } = [];

    /// <summary>
    /// Why the semantic pass was skipped, when the user asked for it and it could not run. Null when
    /// there is nothing worth saying - a plain text file failing to parse as JSON is not news.
    /// </summary>
    public string? SemanticFallbackReason { get; init; }

    /// <summary>
    /// The same changes as <see cref="SemanticChanges"/>, but with spans into each side's text exactly
    /// as it was given - not the pretty-printed copy <see cref="Left"/>/<see cref="Right"/> hold for
    /// alignment. This is what the Json view highlights from, since it shows each document unaligned
    /// and untouched rather than reformatted to line up with the other side. Empty when semantic
    /// comparison did not run.
    /// </summary>
    public IReadOnlyList<JsonChange> OriginalSemanticChanges { get; init; } = [];

    /// <summary>The left side's text exactly as given, before any canonicalisation for alignment.</summary>
    public string OriginalLeftText { get; init; } = string.Empty;

    /// <summary>The right side's text exactly as given.</summary>
    public string OriginalRightText { get; init; } = string.Empty;

    /// <summary>Nothing loaded yet - the app's initial state.</summary>
    public static FileComparison Empty { get; } = new(
        TextDocument.Empty,
        TextDocument.Empty,
        ComparisonOptions.Default,
        DiffResult.Empty);

    /// <summary>True once both sides have a real file behind them.</summary>
    public bool HasBothSides =>
        !string.IsNullOrEmpty(Left.Path) && !string.IsNullOrEmpty(Right.Path);
}
