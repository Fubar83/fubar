using System.Collections.Generic;
using System.Linq;
using DiffPlex;
using DiffPlex.Model;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Infrastructure.Comparison;

/// <summary>
/// <see cref="IInlineDiffEngine"/> over DiffPlex's word differ.
///
/// Word granularity rather than character: a changed identifier should highlight as one word, not as a
/// scatter of individual letters with unchanged ones showing through. DiffPlex chunks the line for us
/// and reports which chunks differ; the work here is turning its chunk indices back into character
/// offsets, which it does not provide directly.
/// </summary>
public sealed class DiffPlexInlineDiffEngine : IInlineDiffEngine
{
    private readonly IDiffer _differ = new Differ();

    public (IReadOnlyList<CharSpan> Left, IReadOnlyList<CharSpan> Right) DiffWithinLine(
        string left,
        string right,
        SourceLanguage language = SourceLanguage.None)
    {
        // Identical lines should not have been routed here (only Modified rows are), but an empty side
        // has no characters to point at either way - bail before allocating anything.
        if (left.Length == 0 && right.Length == 0)
        {
            return ([], []);
        }

        // Normalisation has already been applied upstream, so ignoreWhiteSpace/ignoreCase stay off
        // here - applying either twice would only risk the two paths disagreeing.
        var result = _differ.CreateDiffs(
            left,
            right,
            ignoreWhiteSpace: false,
            ignoreCase: false,
            ChunkerFor(language));

        return (
            ToSpans(result.PiecesOld, result.DiffBlocks, isLeft: true),
            ToSpans(result.PiecesNew, result.DiffBlocks, isLeft: false));
    }

    /// <summary>
    /// The chunker for a language: the real tokeniser where there is one, and the generic punctuation
    /// split everywhere else. Cached per language rather than built per line - this runs once for every
    /// modified row in the document.
    /// </summary>
    private static IChunker ChunkerFor(SourceLanguage language) =>
        language == SourceLanguage.None ? PunctuationChunker.Instance : Chunkers[(int)language];

    /// <summary>
    /// One chunker per language, indexed by the enum rather than switched on it, so adding a language
    /// to <see cref="SourceLanguage"/> needs no change here at all. Built once - this runs for every
    /// modified row in a document.
    /// </summary>
    private static readonly SourceTokenChunker[] Chunkers =
        [.. System.Enum.GetValues<SourceLanguage>().Select(language => new SourceTokenChunker(language))];

    /// <summary>
    /// Splits a line on the language's own token boundaries.
    ///
    /// What this buys over the punctuation chunker below is entirely in the operators. Splitting on
    /// every punctuation character makes <c>=&gt;</c> two chunks, so a lambda whose arrow moved
    /// highlights as two unrelated fragments; worse, changing <c>==</c> to <c>===</c> highlights only
    /// the third character, because the first two matched as separate chunks - a comparison operator
    /// changing meaning is exactly the edit a reviewer must not miss.
    ///
    /// Strings and comments are deliberately broken down FURTHER, into words. They are single tokens to
    /// the scanner, and rightly so, but a sentence in a message string is not one indivisible thing to
    /// a reader - highlighting an entire error message because one word in it changed is the noise this
    /// whole feature exists to remove.
    /// </summary>
    private sealed class SourceTokenChunker : IChunker
    {
        private readonly SourceLanguage _language;

        public SourceTokenChunker(SourceLanguage language) => _language = language;

        public IReadOnlyList<string> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return [];
            }

            var tokens = SourceScanner.ScanLine(text, _language);
            var chunks = new List<string>(tokens.Count);

            foreach (var token in tokens)
            {
                if (token.Kind is SourceTokenKind.String or SourceTokenKind.Comment)
                {
                    AppendWords(text, token, chunks);
                    continue;
                }

                chunks.Add(token.TextIn(text));
            }

            return chunks;
        }

        /// <summary>
        /// Adds a token's text split on whitespace runs, each run kept as its own chunk so the
        /// concatenation still equals the input - the offsets downstream are computed from chunk
        /// lengths, so losing a character here would shift every span after it.
        /// </summary>
        private static void AppendWords(string text, SourceToken token, List<string> chunks)
        {
            var start = token.Start;

            for (var i = token.Start; i < token.End; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    continue;
                }

                if (i > start)
                {
                    chunks.Add(text[start..i]);
                }

                var space = i;
                while (space < token.End && char.IsWhiteSpace(text[space]))
                {
                    space++;
                }

                chunks.Add(text[i..space]);
                start = space;
                i = space - 1;
            }

            if (start < token.End)
            {
                chunks.Add(text[start..token.End]);
            }
        }
    }

    /// <summary>
    /// Splits a line into words, treating punctuation as a boundary but keeping each separator as its
    /// own chunk so the offsets still add up to the original string.
    ///
    /// DiffPlex's built-in <c>WordChunker</c> splits on whitespace and a little punctuation, but not
    /// on quotes or colons - so in <c>"key": "value"</c> the whole quoted run reads as one token and
    /// changing the value highlights the key along with it. Structured text is the common case for a
    /// diff tool, hence a chunker of our own.
    /// </summary>
    private sealed class PunctuationChunker : IChunker
    {
        public static PunctuationChunker Instance { get; } = new();

        private static readonly char[] Separators =
            [' ', '\t', '.', ',', ';', ':', '(', ')', '[', ']', '{', '}', '<', '>', '"', '\'', '=', '/', '\\', '-', '+', '|'];

        public IReadOnlyList<string> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return [];
            }

            var chunks = new List<string>();
            var start = 0;

            for (var i = 0; i < text.Length; i++)
            {
                if (System.Array.IndexOf(Separators, text[i]) < 0)
                {
                    continue;
                }

                if (i > start)
                {
                    chunks.Add(text[start..i]);
                }

                // The separator is emitted as its own chunk rather than dropped: the concatenation of
                // all chunks must equal the input, or the offsets computed from their lengths would
                // drift from the real string.
                chunks.Add(text[i].ToString());
                start = i + 1;
            }

            if (start < text.Length)
            {
                chunks.Add(text[start..]);
            }

            return chunks;
        }
    }

    /// <summary>
    /// Converts DiffPlex's chunk-index ranges into character offsets.
    ///
    /// DiffPlex reports positions as indices into the chunk array, so the character offset of a chunk
    /// is the summed length of everything before it. A running prefix table gives that in one pass
    /// rather than re-summing per block.
    /// </summary>
    private static IReadOnlyList<CharSpan> ToSpans(
        IReadOnlyList<string> pieces,
        IList<DiffBlock> blocks,
        bool isLeft)
    {
        if (pieces.Count == 0 || blocks.Count == 0)
        {
            return [];
        }

        var offsets = new int[pieces.Count + 1];
        for (var i = 0; i < pieces.Count; i++)
        {
            offsets[i + 1] = offsets[i] + pieces[i].Length;
        }

        var spans = new List<CharSpan>();
        foreach (var block in blocks)
        {
            var start = isLeft ? block.DeleteStartA : block.InsertStartB;
            var count = isLeft ? block.DeleteCountA : block.InsertCountB;

            // A block can be one-sided: a pure insertion has no deleted chunks, so this side has
            // nothing to highlight for it.
            if (count == 0)
            {
                continue;
            }

            var end = start + count;
            if (start < 0 || end > pieces.Count)
            {
                continue;
            }

            var length = offsets[end] - offsets[start];
            if (length > 0)
            {
                spans.Add(new CharSpan(
                    offsets[start],
                    length,
                    isLeft ? ChangeKind.Deleted : ChangeKind.Inserted));
            }
        }

        return spans;
    }
}
