using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Merge;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// Orchestrates read -> canonicalise -> key -> align twice -> merge.
///
/// Thin, like its two-way sibling, and for the same reason: the merge rules belong to
/// <see cref="ThreeWayMerger"/> and the comparison rules to the normalizer. What this owns is the
/// ORDER, and one decision worth naming - the two alignments are produced by the SAME
/// <see cref="IDiffEngine"/>, under the SAME options, as an ordinary two-way diff. That is what keeps
/// a three-way merge consistent with what the user sees when they compare any two of the same files:
/// every comparison option, every code rule, and the slider are already baked into the keys and the
/// rows before the merge ever looks at them.
/// </summary>
public sealed class ThreeWayComparisonService : IThreeWayComparisonService
{
    private readonly ITextFileReader _reader;
    private readonly IDiffEngine _engine;
    private readonly ILineNormalizer _normalizer;

    public ThreeWayComparisonService(ITextFileReader reader, IDiffEngine engine, ILineNormalizer normalizer)
    {
        _reader = reader;
        _engine = engine;
        _normalizer = normalizer;
    }

    public async Task<ThreeWayComparison> CompareFilesAsync(
        string ancestorPath,
        string leftPath,
        string rightPath,
        ComparisonOptions options,
        CancellationToken cancellationToken = default)
    {
        // Sequential rather than in parallel: a failure has to name the file that failed, and reading
        // three local files is not the bottleneck worth complicating error handling for.
        var ancestor = await _reader.ReadAsync(ancestorPath, cancellationToken).ConfigureAwait(false);
        var left = await _reader.ReadAsync(leftPath, cancellationToken).ConfigureAwait(false);
        var right = await _reader.ReadAsync(rightPath, cancellationToken).ConfigureAwait(false);

        // Off the calling thread: this is two full diffs plus a merge, and on the UI thread a large
        // trio freezes the window including the cancel path that would let the user escape it.
        return await Task
            .Run(() => Compare(ancestor, left, right, options), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ThreeWayComparison> RecompareAsync(
        ThreeWayComparison comparison,
        ComparisonOptions options,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Compare(comparison.Ancestor, comparison.Left, comparison.Right, options),
            cancellationToken);

    private ThreeWayComparison Compare(
        TextDocument ancestor,
        TextDocument left,
        TextDocument right,
        ComparisonOptions options)
    {
        // Canonicalisation can change the line count, so it happens first and produces the documents
        // that are both merged AND displayed.
        var ancestorDoc = ancestor with { Lines = _normalizer.Canonicalize(ancestor.Lines, options) };
        var leftDoc = left with { Lines = _normalizer.Canonicalize(left.Lines, options) };
        var rightDoc = right with { Lines = _normalizer.Canonicalize(right.Lines, options) };

        // The two edits decide the language; the ancestor only gets a say when neither of them has an
        // extension worth anything, since it is the one document nobody is editing.
        var language = LanguageDetector.ForPair(leftDoc.Path, rightDoc.Path);
        if (language == SourceLanguage.None)
        {
            language = LanguageDetector.FromPath(ancestorDoc.Path);
        }

        var ancestorKeys = KeysFor(ancestorDoc.Lines, language, options);
        var leftKeys = KeysFor(leftDoc.Lines, language, options);
        var rightKeys = KeysFor(rightDoc.Lines, language, options);

        var toLeft = Align(ancestorKeys, ancestorDoc.Lines, leftKeys, leftDoc.Lines, options);
        var toRight = Align(ancestorKeys, ancestorDoc.Lines, rightKeys, rightDoc.Lines, options);

        var result = ThreeWayMerger.Merge(
            new MergeDocument(ancestorKeys, ancestorDoc.Lines),
            new MergeDocument(leftKeys, leftDoc.Lines),
            new MergeDocument(rightKeys, rightDoc.Lines),
            toLeft,
            toRight);

        return new ThreeWayComparison(ancestorDoc, leftDoc, rightDoc, options, result)
        {
            Language = language,
        };
    }

    /// <summary>
    /// One two-way alignment, compacted exactly as the two-way view compacts it.
    ///
    /// Sliding matters MORE here than it does for a plain diff. A merge region is "everything between
    /// two points where the ancestor still lines up", so where a one-sided group sits decides where a
    /// region starts and stops - and a group parked across a block boundary produces a region that
    /// straddles two, which is then presented as one decision. Content-neutral either way (see
    /// <see cref="ChangeGroupSlider"/>): it only ever moves a group across a line identical to the one
    /// leaving it, so which lines the merge considers unchanged is exactly the same set afterwards.
    /// </summary>
    private IReadOnlyList<Core.Models.DiffLine> Align(
        IReadOnlyList<string> ancestorKeys,
        IReadOnlyList<string> ancestorLines,
        IReadOnlyList<string> otherKeys,
        IReadOnlyList<string> otherLines,
        ComparisonOptions options) =>
        ChangeGroupSlider.Compact(
            _engine.Align(ancestorKeys, otherKeys, options),
            ancestorKeys,
            ancestorLines,
            otherKeys,
            otherLines);

    /// <summary>
    /// The comparison key per line: the code rules first (comments stripped when asked), then the
    /// text-level normalisation. Never displayed - the rows carry each document's own lines.
    /// </summary>
    private string[] KeysFor(IReadOnlyList<string> lines, SourceLanguage language, ComparisonOptions options)
    {
        var code = CodeLines.Analyze(lines, language, options.Code);
        var source = code?.ComparisonLines ?? lines;

        var keys = new string[source.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = _normalizer.ToComparisonKey(source[i], options);
        }

        return keys;
    }
}
