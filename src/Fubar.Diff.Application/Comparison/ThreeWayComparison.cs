using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Merge;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// A completed three-way merge: all three loaded documents, the options used, and the result. Keeping
/// the documents alongside the result is what lets an option be changed without touching the disk
/// again - the same bargain <see cref="FileComparison"/> makes.
/// </summary>
/// <param name="Ancestor">The common ancestor - what both edits started from.</param>
/// <param name="Left">One edit. "Theirs", by the convention the two-way view already uses.</param>
/// <param name="Right">The other edit. "Mine".</param>
/// <param name="Options">The options this result was produced under.</param>
/// <param name="Result">The merged rows and regions.</param>
public sealed record ThreeWayComparison(
    TextDocument Ancestor,
    TextDocument Left,
    TextDocument Right,
    ComparisonOptions Options,
    ThreeWayResult Result)
{
    /// <summary>The source language all three were compared as, from their file extensions.</summary>
    public SourceLanguage Language { get; init; } = SourceLanguage.None;

    /// <summary>Nothing loaded yet.</summary>
    public static ThreeWayComparison Empty { get; } = new(
        TextDocument.Empty,
        TextDocument.Empty,
        TextDocument.Empty,
        ComparisonOptions.Default,
        ThreeWayResult.Empty);

    /// <summary>True once all three sides have a real file behind them.</summary>
    public bool HasAllSides =>
        !string.IsNullOrEmpty(Ancestor.Path)
        && !string.IsNullOrEmpty(Left.Path)
        && !string.IsNullOrEmpty(Right.Path);

    /// <summary>The document one side refers to, for a caption or a save destination.</summary>
    public TextDocument DocumentFor(MergeSide side) => side switch
    {
        MergeSide.Left => Left,
        MergeSide.Right => Right,
        _ => Ancestor,
    };
}
