using System;
using System.Collections.Generic;
using Fubar.Diff.Core.Code;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Languages;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// What a structural comparison found, or why it did not run.
/// </summary>
/// <param name="Applied">Whether both sides parsed and were compared structurally.</param>
/// <param name="Changes">What happened to each member. Empty unless <paramref name="Applied"/>.</param>
/// <param name="Summary">The counts, and the "no functional changes" answer.</param>
/// <param name="SkippedReason">
/// Why it did not run, when that is worth saying. Null for the ordinary cases - the file is not C#,
/// or the feature is off - because neither is news.
/// </param>
public readonly record struct CodeStructureResult(
    bool Applied,
    IReadOnlyList<CodeChange> Changes,
    CodeStructureSummary Summary,
    string? SkippedReason)
{
    /// <summary>Nothing ran.</summary>
    public static CodeStructureResult None { get; } = new(false, [], CodeStructureSummary.None, null);

    /// <summary>Nothing ran, and there is a reason worth showing.</summary>
    public static CodeStructureResult Skipped(string reason) =>
        new(false, [], CodeStructureSummary.None, reason);
}

/// <summary>
/// Runs the structural comparison alongside the text one, when the pair is source code the parser
/// can read.
///
/// A refinement, not a second pipeline - the same relationship <see cref="JsonSemanticPass"/> has to
/// the text diff, and for the same reason. The alignment, the hunks, the merge, the patch and every
/// renderer are untouched by this; what it produces is an ANSWER ABOUT the comparison ("two methods
/// changed, everything else was reformatted") rather than a different comparison. That is what makes
/// it safe to have on by default: the worst case is a panel with nothing in it.
///
/// It differs from the JSON pass in one way worth stating, because it looks like an oversight and is
/// not: it does NOT mark rows, filter them or change any count. JSON's semantic pass decides which
/// text rows count as differences, because two JSON documents that differ only in property order are
/// genuinely the same document. Two C# files that differ only in member order are NOT the same file -
/// the bytes on disk differ, a code review is about those bytes, and quietly reporting them as equal
/// would be the tool lying about what it was shown. So the text diff keeps saying exactly what
/// changed, and this says what it MEANT.
/// </summary>
public sealed class CodeStructurePass
{
    private readonly ICodeStructureParser? _parser;

    /// <param name="parser">
    /// Optional, like every other adapter taken here: without one the pass is inert, which is what a
    /// host that only compares text - and every test that only cares about text - should get, rather
    /// than being made to supply a compiler front end.
    /// </param>
    public CodeStructurePass(ICodeStructureParser? parser = null) => _parser = parser;

    /// <summary>
    /// The size past which a side is not parsed, in characters.
    ///
    /// Measured, not guessed. Parsing and walking a pair of files costs roughly a quarter of a
    /// millisecond per kilobyte - about 15 ms for two ordinary 1,000-line source files, 200 ms for
    /// two 10,000-line ones, and half a second for a 2 MB pair. The cap sits where the cost starts
    /// to exceed the whole rest of the pipeline, and where the answer stops being useful anyway: a
    /// file that size is generated, and a structure tree with ten thousand members in it is not
    /// something anyone reads.
    /// </summary>
    internal const int MaxLength = 1_000_000;

    /// <summary>
    /// Compares two files structurally, when they are source the parser understands.
    ///
    /// Both sides must parse. One that does not is not a failure to report loudly - a file mid-edit
    /// is the most ordinary thing there is to be diffing - but it does mean there is nothing to
    /// compare against, and half a structure tree would be worse than none.
    /// </summary>
    public CodeStructureResult Apply(
        string leftText,
        string rightText,
        SourceLanguage language,
        ComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_parser is null || !options.Code.Structure || !_parser.CanParse(language))
        {
            return CodeStructureResult.None;
        }

        if (leftText.Length > MaxLength || rightText.Length > MaxLength)
        {
            return CodeStructureResult.Skipped("The files are too large to analyse structurally.");
        }

        if (!_parser.TryParse(leftText, language, out var left) ||
            !_parser.TryParse(rightText, language, out var right))
        {
            return CodeStructureResult.Skipped("One of the files could not be parsed as source code.");
        }

        var changes = CodeStructureDiffer.Compare(left!, right!);

        return new CodeStructureResult(true, changes, CodeStructureSummary.Of(changes), null);
    }
}
