using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
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
    private readonly IBinaryFileReader? _binaryReader;
    private readonly IDiffEngine _engine;
    private readonly IInlineDiffEngine _inlineEngine;
    private readonly ILineNormalizer _normalizer;
    private readonly JsonSemanticPass _semanticPass;
    private readonly CodeStructurePass _structurePass;

    public FileComparisonService(
        ITextFileReader reader,
        IDiffEngine engine,
        IInlineDiffEngine inlineEngine,
        ILineNormalizer normalizer,
        JsonSemanticPass semanticPass,
        IBinaryFileReader? binaryReader = null,
        CodeStructurePass? structurePass = null)
    {
        _reader = reader;
        _engine = engine;
        _inlineEngine = inlineEngine;
        _normalizer = normalizer;
        _semanticPass = semanticPass;

        // Optional for the same reason the binary reader is: a caller that only compares text should
        // not have to supply a compiler front end to do it. Without one the structural panel is simply
        // never populated, which is what every non-C# comparison gets anyway.
        _structurePass = structurePass ?? new CodeStructurePass();

        // Optional so a caller that only ever compares text - and every test that only cares about
        // text - is not made to supply one. Without it a binary file is refused exactly as it was
        // before this existed, which is the correct degradation rather than a crash.
        _binaryReader = binaryReader;
    }

    public async Task<FileComparison> CompareFilesAsync(
        string leftPath,
        string rightPath,
        ComparisonOptions options,
        CancellationToken cancellationToken = default)
    {
        TextDocument left;
        TextDocument right;

        try
        {
            // Sequential rather than Task.WhenAll: a failure must name the file that failed, and reading
            // two local files is not the bottleneck worth complicating error handling for.
            left = await _reader.ReadAsync(leftPath, cancellationToken).ConfigureAwait(false);
            right = await _reader.ReadAsync(rightPath, cancellationToken).ConfigureAwait(false);
        }
        catch (TextFileReadException ex) when (ex.IsBinary && _binaryReader is not null)
        {
            // Not text after all. Comparing the bytes is a far better answer than the error the text
            // reader was about to produce, and it is the same pair of files the user asked about -
            // so it comes back as a comparison rather than as a failure with a suggestion attached.
            return await CompareBytesAsync(leftPath, rightPath, options, cancellationToken).ConfigureAwait(false);
        }

        // Off the calling thread. Diffing is CPU-bound and grows with file size; on the UI thread a
        // large pair freezes the window, including the cancel path that would let the user escape it.
        return await Task.Run(() => Compare(left, right, options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Compares the two files as bytes.
    ///
    /// Reads BOTH sides even though only one may have been binary: a PNG against a text file is still a
    /// pair the user asked to compare, and the only comparison of it that means anything is at the byte
    /// level. Saying "the left one is binary" and stopping would be technically accurate and useless.
    /// </summary>
    private async Task<FileComparison> CompareBytesAsync(
        string leftPath,
        string rightPath,
        ComparisonOptions options,
        CancellationToken cancellationToken)
    {
        var left = await _binaryReader!.ReadAsync(leftPath, cancellationToken).ConfigureAwait(false);
        var right = await _binaryReader.ReadAsync(rightPath, cancellationToken).ConfigureAwait(false);

        var comparison = await Task
            .Run(() => BinaryComparer.Compare(left, right), cancellationToken)
            .ConfigureAwait(false);

        // Empty text documents carrying the paths: everything in the UI that names a comparison, tracks
        // its files or lists it as recent reads them, and none of that has any business knowing whether
        // the content was text.
        return new FileComparison(
            new TextDocument(leftPath, [], TextFormat.Default),
            new TextDocument(rightPath, [], TextFormat.Default),
            options,
            DiffResult.Empty)
        {
            Binary = comparison,
        };
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

    public JsonDisplay FormatJsonForDisplay(
        FileComparison comparison,
        bool prettyLeft,
        bool prettyRight,
        JsonFormatOptions format)
    {
        var left = comparison.OriginalLeftText;
        var right = comparison.OriginalRightText;

        if (!comparison.IsSemantic || (!prettyLeft && !prettyRight))
        {
            return new JsonDisplay(left, right, comparison.OriginalSemanticChanges);
        }

        var formattedLeft = prettyLeft ? _semanticPass.TryFormat(left, format) ?? left : left;
        var formattedRight = prettyRight ? _semanticPass.TryFormat(right, format) ?? right : right;

        // Re-derived against the text that will actually be shown. Skipping this would leave every
        // highlight pointing at the line a value used to be on, which looks exactly like the
        // comparison having gone wrong.
        var changes = _semanticPass.TryCompareOriginalText(formattedLeft, formattedRight, comparison.Options)
                      ?? comparison.OriginalSemanticChanges;

        return new JsonDisplay(formattedLeft, formattedRight, changes);
    }

    public Task<FileComparison> CompareDocumentsAsync(
        TextDocument left,
        TextDocument right,
        ComparisonOptions options,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Compare(left, right, options), cancellationToken);

    /// <summary>
    /// Re-runs the comparison off the calling thread. The synchronous <see cref="Recompare"/> stays for
    /// callers that are already on a background thread, and for tests.
    /// </summary>
    public Task<FileComparison> RecompareAsync(
        FileComparison comparison,
        ComparisonOptions options,
        CancellationToken cancellationToken = default) =>
        comparison.IsBinary
            ? Task.FromResult(WithOptions(comparison, options))
            : Task.Run(
                () => Compare(comparison.Left, comparison.Right, options, comparison.OriginalLeftText, comparison.OriginalRightText),
                cancellationToken);

    public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) =>
        comparison.IsBinary
            ? WithOptions(comparison, options)
            : Compare(comparison.Left, comparison.Right, options, comparison.OriginalLeftText, comparison.OriginalRightText);

    /// <summary>
    /// A binary comparison under new options, which is the same comparison: not one of the text options
    /// means anything to a stream of bytes.
    ///
    /// It has to be handled rather than left to fall through, and the reason is nasty. A binary result
    /// carries EMPTY text documents, so re-running the text path over them would succeed, produce an
    /// empty diff, and drop <see cref="FileComparison.Binary"/> - the tab would quietly turn from a
    /// picture into "the files are identical" the moment anyone ticked "ignore whitespace".
    /// </summary>
    private static FileComparison WithOptions(FileComparison comparison, ComparisonOptions options) =>
        comparison with { Options = options };

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

        // How each side should be READ, which is a different question from what language it is:
        // JSON is recognised by trying to parse, YAML only by its name. Per side, so a JSON config
        // can be compared against its YAML translation - see StructuredFormatDetector.
        var leftFormat = StructuredFormatDetector.For(leftDoc.Path, options.Mode);
        var rightFormat = StructuredFormatDetector.For(rightDoc.Path, options.Mode);

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
            options,
            leftFormat,
            rightFormat);

        // Original text parses whenever the canonicalized text did: canonicalization either changed
        // nothing (same text, so the same parse result) or it succeeded, which requires the original
        // to have parsed in the first place to produce that output. The ?? is defensive, not expected.
        var originalSemanticChanges = semantic.Applied
            ? _semanticPass.TryCompareOriginalText(trueOriginalLeftText, trueOriginalRightText, options, leftFormat, rightFormat) ?? semantic.Changes
            : [];

        // On the text exactly as given, never the canonicalized copy: a structural answer about a
        // document the user cannot see would name members at lines that are not there, and
        // "reformatted" would be reporting the reformatting this pipeline just did.
        var structure = _structurePass.Apply(trueOriginalLeftText, trueOriginalRightText, language, options);

        return new FileComparison(leftDoc, rightDoc, options, semantic.Result)
        {
            Language = language,
            CodeChanges = structure.Changes,
            CodeSummary = structure.Summary,
            CodeStructureSkippedReason = structure.SkippedReason,

            // What each side was actually READ as, and only when the pass ran - so "compared as text"
            // stays distinguishable from "compared as JSON that happened to have no differences".
            LeftFormat = semantic.Applied ? leftFormat : StructuredFormat.None,
            RightFormat = semantic.Applied ? rightFormat : StructuredFormat.None,
            IsSemantic = semantic.Applied,
            SemanticChanges = semantic.Changes,
            SemanticFallbackReason = semantic.FallbackReason,
            OriginalSemanticChanges = originalSemanticChanges,
            OriginalLeftText = trueOriginalLeftText,
            OriginalRightText = trueOriginalRightText,

            // From the original text, like the changes the tree is built from - the paths are
            // structural either way, but scanning the same documents keeps the two in step if that
            // ever stops being true.
            ArrayKeys = semantic.Applied
                ? _semanticPass.ScanArrays(trueOriginalLeftText, trueOriginalRightText, options, leftFormat, rightFormat)
                : new Dictionary<string, ArrayKeyChoices>(),

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
    ///
    /// <para>Also the one place that can see a row was equalised BY AN OPTION. The engine matched on
    /// comparison keys; here both raw lines are in hand for the first time, so an Unchanged row whose
    /// two texts differ can only have been made equal by ignore-whitespace, ignore-case,
    /// ignore-comments, a line-pattern mask or Unicode normalisation. Marking it
    /// <see cref="DiffLine.IsIgnored"/> gets it the same faint band an ignored JSON path already gets -
    /// and costs one ordinal string compare per unchanged row, which is nothing beside the alignment
    /// that just ran.</para>
    ///
    /// <para>Without it, turning an option on made the difference vanish completely, and the reader
    /// could not tell "these lines agree" from "these lines disagree and I asked not to be told" - the
    /// same wrong silence an ignored reorder used to have. It also makes an option's effect visible
    /// while it is on, which is the only way to check a rule is doing what you thought.</para>
    /// </summary>
    private static List<DiffLine> ProjectOntoDocuments(
        IReadOnlyList<DiffLine> rows,
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines)
    {
        var projected = new List<DiffLine>(rows.Count);
        foreach (var row in rows)
        {
            var leftText = row.LeftNumber is { } l ? leftLines[l - 1] : null;
            var rightText = row.RightNumber is { } r ? rightLines[r - 1] : null;

            projected.Add(row with
            {
                LeftText = leftText,
                RightText = rightText,

                // Never overwrite an IsIgnored the semantic pass already decided; only ADD the ones the
                // text options are responsible for.
                IsIgnored = row.IsIgnored || EqualisedByAnOption(row, leftText, rightText),
            });
        }

        return projected;
    }

    /// <summary>
    /// True for a row the aligner called equal whose two lines are not actually the same text.
    ///
    /// Restricted to <see cref="ChangeKind.Unchanged"/>: a filler has no counterpart to differ from, and
    /// a row that is already reported as a change needs no faint hint that it differs.
    /// </summary>
    private static bool EqualisedByAnOption(DiffLine row, string? leftText, string? rightText) =>
        row.Kind == ChangeKind.Unchanged
        && leftText is not null
        && rightText is not null
        && !string.Equals(leftText, rightText, StringComparison.Ordinal);

    /// <summary>
    /// Longest line the character-level differ is asked about.
    ///
    /// Far above any line a person writes - the longest lines in real source are a few thousand
    /// characters - and far below a minified file, which is one line holding the whole bundle. See
    /// <see cref="WithInlineSpans"/> for the measurements.
    /// </summary>
    internal const int MaxInlineDiffLength = 20_000;

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

            // A line too long to pick words out of. Character diffing is O(length x edits), which is
            // free on source code and ruinous on a minified bundle: measured at 340 ms for a heavily
            // changed 250,000-character line and 8.3 SECONDS at 1.3 million - per row, on a file that
            // may be nothing but such rows. The row still gets its change tint, so the difference is
            // reported; what is skipped is the character-level refinement, which on one line holding
            // an entire bundle would have highlighted most of it anyway.
            if (left.Length > MaxInlineDiffLength || right.Length > MaxInlineDiffLength)
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
