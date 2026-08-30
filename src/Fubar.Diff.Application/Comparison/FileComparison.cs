using System.Collections.Generic;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Languages;
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
    /// <summary>
    /// The source language the pair was compared as, from their file extensions.
    ///
    /// <see cref="SourceLanguage.None"/> for everything the scanner does not know, which is most files
    /// and is not a failure - it just means the code rules had nothing to apply and the comparison ran
    /// as plain text. Carried on the result so the UI can say which rules were in play without
    /// re-deriving it from the paths and risking a different answer.
    /// </summary>
    public SourceLanguage Language { get; init; } = SourceLanguage.None;

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

    /// <summary>
    /// How the two files' encodings, byte order marks, line endings and trailing newlines differ.
    ///
    /// Computed rather than derived from <see cref="Result"/> because it CANNOT be: the reader strips
    /// the BOM and splits on every terminator, so two files differing only in these ways produce
    /// identical lines and an empty diff. Without this the tool would report "identical" about files
    /// that are not - see <see cref="TextFormatComparer"/>.
    /// </summary>
    public TextFormatDifference FormatDifference { get; init; } = TextFormatDifference.None;

    /// <summary>
    /// True when the content matches line for line but the files still differ on disk. The distinction
    /// the status line needs: "identical" would be wrong, and showing a diff with no rows would be
    /// unhelpful, so this case gets said out loud instead.
    /// </summary>
    public bool DiffersOnlyByFormat => Result.AreIdentical && FormatDifference.Any;

    /// <summary>
    /// The byte-level comparison, when at least one side turned out not to be text. Null for the
    /// ordinary case.
    ///
    /// Carried on the same result rather than returned from a separate service so that everything the
    /// UI already does with a comparison - open it in a tab, name it, watch its files, reload it -
    /// keeps working without learning about a second kind of comparison. What differs is only what is
    /// DRAWN, which is one more view mode.
    ///
    /// When this is set, <see cref="Left"/> and <see cref="Right"/> carry the paths and no lines, and
    /// <see cref="Result"/> is empty: there is no text to align, and inventing rows for bytes would put
    /// a diff on screen that means nothing.
    /// </summary>
    public BinaryComparison? Binary { get; init; }

    /// <summary>True when this comparison is of bytes rather than of text.</summary>
    public bool IsBinary => Binary is not null;

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
