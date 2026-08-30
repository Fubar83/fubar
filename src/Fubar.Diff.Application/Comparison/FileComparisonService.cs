using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Languages;
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

        // Off the calling thread. Diffing is CPU-bound and grows with file size; on the UI thread a
        // large pair freezes the window, including the cancel path that would let the user escape it.
        return await Task.Run(() => Compare(left, right, options), cancellationToken).ConfigureAwait(false);
    }

    public Task<FileComparison> CompareTextAsync(
        string leftText,
        string rightText,
        ComparisonOptions options,
        string leftLabel = "left",
        string rightLabel = "right",
        CancellationToken cancellationToken = default)
    {
        // The labels go in the Path slot so DisplayName shows something meaningful in a title or tab.
        // TextFormat.Default is right here: this content never came from a file, so there is no
        // encoding, BOM or terminator to preserve - and nothing will be saved back over one.
        var left = new TextDocument(leftLabel, SplitLines(leftText), TextFormat.Default);
        var right = new TextDocument(rightLabel, SplitLines(rightText), TextFormat.Default);

        return Task.Run(() => Compare(left, right, options), cancellationToken);
    }

    /// <summary>
    /// Splits on any of the three terminators, dropping the empty string a trailing one would leave -
    /// matching how <c>ITextFileReader</c> treats a file, so both paths produce the same line count for
    /// the same content.
    /// </summary>
    private static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split(["\r\n", "\n", "\r"], System.StringSplitOptions.None);

        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    /// <summary>
    /// Re-runs the comparison off the calling thread. The synchronous <see cref="Recompare"/> stays for
    /// callers that are already on a background thread, and for tests.
    /// </summary>
    public Task<FileComparison> RecompareAsync(
        FileComparison comparison,
        ComparisonOptions options,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Compare(comparison.Left, comparison.Right, options, comparison.OriginalLeftText, comparison.OriginalRightText),
            cancellationToken);

    public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) =>
        Compare(comparison.Left, comparison.Right, options, comparison.OriginalLeftText, comparison.OriginalRightText);

    private FileComparison Compare(
        TextDocument left,
        TextDocument right,
        ComparisonOptions options,
        string? originalLeftText = null,
        string? originalRightText = null)
    {
        // A fresh compare has no prior result to carry the true original text forward from, so it
        // comes from the documents just read; a recompare (an option changed, not the content) is
        // passed its PREVIOUS result's original text explicitly - otherwise, since Left/Right below
        // hold the CANONICALIZED text, "original" would silently become "canonicalized" the moment
        // any option was toggled, one recompare after the file was actually loaded.
        var trueOriginalLeftText = originalLeftText ?? string.Join('\n', left.Lines);
        var trueOriginalRightText = originalRightText ?? string.Join('\n', right.Lines);

        // Canonicalisation can change the line count, so it happens first and produces the documents
        // that are both compared AND displayed. Everything after this works against these lines.
        //
        // Deliberately NOT reformatted for JSON automatically: Text mode shows the file as it is,
        // full stop. When the two sides are formatted so differently that line alignment has nothing
        // sane to match (a minified file against a pretty one), that is what the Json view is FOR -
        // it needs no alignment at all, and it is the default the moment semantic comparison applies.
        // Silently rewriting the user's content to paper over Text mode's own limitation was solving
        // the problem in the wrong view.
        var leftLines = _normalizer.Canonicalize(left.Lines, options);
        var rightLines = _normalizer.Canonicalize(right.Lines, options);

        var leftDoc = left with { Lines = leftLines };
        var rightDoc = right with { Lines = rightLines };

        // From the file extensions, not the content: see LanguageDetector for why guessing is worse
        // than not knowing. None for anything unrecognised, which turns every code rule below into a
        // no-op rather than into a wrong answer.
        var language = LanguageDetector.ForPair(leftDoc.Path, rightDoc.Path);

        // Null unless a code rule is actually switched on, so an ordinary comparison never pays for
        // a scan it will not consult.
        var leftCode = CodeLines.Analyze(leftDoc.Lines, language, options.Code);
        var rightCode = CodeLines.Analyze(rightDoc.Lines, language, options.Code);

        // Keys again, not display text: ComparisonLines is the document with its comments stripped
        // when the user asked for that, and it is never shown to anyone - the projection below puts
        // the real lines back, comments included.
        // Compiled once per comparison rather than per line: these are user-supplied regexes, and
        // rebuilding them 60,000 times would dwarf the diff itself.
        var mask = LinePatternMask.Create(options.IgnoredLinePatterns);

        var leftKeys = ToKeys(leftCode?.ComparisonLines ?? leftDoc.Lines, options, mask);
        var rightKeys = ToKeys(rightCode?.ComparisonLines ?? rightDoc.Lines, options, mask);

        var rows = _engine.Align(leftKeys, rightKeys, options);

        // Before projection, so the slider judges "are these two lines interchangeable" on the same
        // keys the engine just matched on. It never changes what the diff SAYS - only where an
        // ambiguous run of added or removed lines sits among the identical lines around it.
        rows = ChangeGroupSlider.Compact(rows, leftKeys, leftDoc.Lines, rightKeys, rightDoc.Lines);

        // After the slider and on the keys, for the same two reasons: a group that has just been slid
        // to a better position is the one that should be matched against its other half, and two lines
        // the user asked to compare as equal must count as equal here too. Always on and never
        // optional - it only ever ADDS a mark to rows that are already reported as changes, so there
        // is nothing for a user to want switched off.
        rows = MoveDetector.Detect(rows, leftKeys, rightKeys);

        var projected = ProjectOntoDocuments(rows, leftDoc.Lines, rightDoc.Lines);

        // Filtered AFTER the rows exist, because a comment that was ADDED has nothing on the other
        // side for a key to match it against - see CodeLineFilter.
        var textResult = CodeLineFilter.Apply(
            DiffResult.Create(WithInlineSpans(projected, language)),
            leftCode,
            rightCode);

        // Semantic refinement runs last, over the finished alignment: it only decides which rows COUNT
        // as changes, so everything downstream sees the same shape either way.
        var semantic = _semanticPass.Apply(
            textResult,
            string.Join('\n', leftDoc.Lines),
            string.Join('\n', rightDoc.Lines),
            options);

        // Original text parses whenever the canonicalized text did: canonicalization either changed
        // nothing (same text, so the same parse result) or it succeeded, which requires the original
        // to have parsed in the first place to produce that output. The ?? is defensive, not expected.
        var originalSemanticChanges = semantic.Applied
            ? _semanticPass.TryCompareOriginalText(trueOriginalLeftText, trueOriginalRightText, options) ?? semantic.Changes
            : [];

        return new FileComparison(leftDoc, rightDoc, options, semantic.Result)
        {
            Language = language,
            IsSemantic = semantic.Applied,
            SemanticChanges = semantic.Changes,
            SemanticFallbackReason = semantic.FallbackReason,
            OriginalSemanticChanges = originalSemanticChanges,
            OriginalLeftText = trueOriginalLeftText,
            OriginalRightText = trueOriginalRightText,

            // Invisible in the lines by construction (the reader consumes the BOM and splits on every
            // terminator), so it has to be carried alongside them or it is lost entirely.
            FormatDifference = TextFormatComparer.Compare(leftDoc.Format, rightDoc.Format),
        };
    }

    /// <summary>
    /// The comparison key per line: the user's ignore patterns first, then the text-level
    /// normalisation the adapter owns.
    ///
    /// Masking BEFORE normalising, because the two compose in that order and not the other: a rule
    /// written against what the user can see should match what they see, not a copy that has already
    /// been trimmed and case-folded out from under it.
    /// </summary>
    private string[] ToKeys(IReadOnlyList<string> lines, ComparisonOptions options, LinePatternMask? mask)
    {
        var keys = new string[lines.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            var line = mask is null ? lines[i] : mask.Apply(lines[i]);
            keys[i] = _normalizer.ToComparisonKey(line, options);
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
    private List<DiffLine> WithInlineSpans(List<DiffLine> rows, SourceLanguage language)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Kind != ChangeKind.Modified || row.LeftText is not { } left || row.RightText is not { } right)
            {
                continue;
            }

            // A row whose sides moved is a pairing of convenience, not of meaning: the aligner put
            // `void Helper()` opposite `void Run()` because they were in the same place, and both have
            // since been recognised as halves of two different blocks. Highlighting the letters that
            // differ between them would invite the reader to read a word-level change that nobody made.
            if (row.IsMoved)
            {
                continue;
            }

            var (leftSpans, rightSpans) = _inlineEngine.DiffWithinLine(left, right, language);
            rows[i] = row with { LeftSpans = leftSpans, RightSpans = rightSpans };
        }

        return rows;
    }
}
