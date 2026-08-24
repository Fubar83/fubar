using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// Orchestrates canonicalise -> key -> align -> project -> refine. Deliberately thin: the algorithm
/// belongs to the engine and the rules to the normalizer. What this owns is the ORDER, and one
/// invariant that is easy to get wrong: the engine matches on comparison KEYS, but every row it
/// produces is projected back onto the document's own lines before anyone sees it. Without that
/// projection, turning on "ignore case" would show the user a lower-cased copy of their own file.
///
/// The final refine step adds character-level spans to modified rows. It runs AFTER projection for
/// exactly the same reason: span offsets must address the display text, since trimming a key shifts
/// every offset in it.
/// </summary>
public sealed class FileComparisonService : IFileComparisonService
{
    private readonly ITextFileReader _reader;
    private readonly IDiffEngine _engine;
    private readonly IInlineDiffEngine _inlineEngine;
    private readonly ILineNormalizer _normalizer;
    private readonly JsonSemanticPass _semanticPass;

    public FileComparisonService(
        ITextFileReader reader,
        IDiffEngine engine,
        IInlineDiffEngine inlineEngine,
        ILineNormalizer normalizer,
        JsonSemanticPass semanticPass)
    {
        _reader = reader;
        _engine = engine;
        _inlineEngine = inlineEngine;
        _normalizer = normalizer;
        _semanticPass = semanticPass;
    }

    public async Task<FileComparison> CompareFilesAsync(
        string leftPath,
        string rightPath,
        ComparisonOptions options,
        CancellationToken cancellationToken = default)
    {
        // Sequential rather than Task.WhenAll: a failure must name the file that failed, and reading
        // two local files is not the bottleneck worth complicating error handling for.
        var left = await _reader.ReadAsync(leftPath, cancellationToken).ConfigureAwait(false);
        var right = await _reader.ReadAsync(rightPath, cancellationToken).ConfigureAwait(false);

        return Compare(left, right, options);
    }

    public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) =>
        Compare(comparison.Left, comparison.Right, options);

    private FileComparison Compare(TextDocument left, TextDocument right, ComparisonOptions options)
    {
        // Canonicalisation can change the line count, so it happens first and produces the documents
        // that are both compared AND displayed. Everything after this works against these lines.
        var leftDoc = left with { Lines = _normalizer.Canonicalize(left.Lines, options) };
        var rightDoc = right with { Lines = _normalizer.Canonicalize(right.Lines, options) };

        var rows = _engine.Align(
            ToKeys(leftDoc.Lines, options),
            ToKeys(rightDoc.Lines, options),
            options);

        var projected = ProjectOntoDocuments(rows, leftDoc.Lines, rightDoc.Lines);
        var textResult = DiffResult.Create(WithInlineSpans(projected));

        // Semantic refinement runs last, over the finished alignment: it only decides which rows COUNT
        // as changes, so everything downstream sees the same shape either way.
        var semantic = _semanticPass.Apply(
            textResult,
            string.Join('\n', leftDoc.Lines),
            string.Join('\n', rightDoc.Lines),
            options);

        return new FileComparison(leftDoc, rightDoc, options, semantic.Result)
        {
            IsSemantic = semantic.Applied,
            SemanticChanges = semantic.Changes,
            SemanticFallbackReason = semantic.FallbackReason,
        };
    }

    private string[] ToKeys(IReadOnlyList<string> lines, ComparisonOptions options)
    {
        var keys = new string[lines.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = _normalizer.ToComparisonKey(lines[i], options);
        }

        return keys;
    }

    /// <summary>
    /// Replaces the keys the engine echoed back with the real document lines, using the line numbers
    /// on each row. Fillers have no number and stay empty.
    /// </summary>
    private static List<DiffLine> ProjectOntoDocuments(
        IReadOnlyList<DiffLine> rows,
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines)
    {
        var projected = new List<DiffLine>(rows.Count);
        foreach (var row in rows)
        {
            projected.Add(row with
            {
                LeftText = row.LeftNumber is { } l ? leftLines[l - 1] : null,
                RightText = row.RightNumber is { } r ? rightLines[r - 1] : null,
            });
        }

        return projected;
    }

    /// <summary>
    /// Adds intra-line spans to modified rows, computed on the DISPLAY text.
    ///
    /// Only <see cref="ChangeKind.Modified"/> rows get spans: on a wholly inserted or deleted line the
    /// entire row is already the change, so picking out words within it would be noise. Rows are
    /// mutated in place in the list to avoid a second full copy of what can be a very long document.
    /// </summary>
    private List<DiffLine> WithInlineSpans(List<DiffLine> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Kind != ChangeKind.Modified || row.LeftText is not { } left || row.RightText is not { } right)
            {
                continue;
            }

            var (leftSpans, rightSpans) = _inlineEngine.DiffWithinLine(left, right);
            rows[i] = row with { LeftSpans = leftSpans, RightSpans = rightSpans };
        }

        return rows;
    }
}
