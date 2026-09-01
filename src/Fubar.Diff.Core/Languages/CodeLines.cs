using System.Collections.Generic;
using System.Text;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Core.Languages;

/// <summary>
/// One document seen through the code rules: the text each line should be MATCHED on, and which lines
/// carry no code at all.
///
/// This is a comparison-key concern, so the usual rule applies with full force - nothing here is ever
/// displayed. Stripping a comment produces a key; the pane still shows the user their own line,
/// comment included, because <c>FileComparisonService</c> projects every row back onto the real
/// document before anyone sees it.
/// </summary>
public sealed class CodeLines
{
    private readonly IReadOnlyList<bool> _ignorable;

    private CodeLines(IReadOnlyList<string> comparisonLines, IReadOnlyList<bool> ignorable)
    {
        ComparisonLines = comparisonLines;
        _ignorable = ignorable;
    }

    /// <summary>
    /// Scans a document under the code rules, or returns null when there is nothing to do - an unknown
    /// language, or both rules off. Null rather than an all-defaults instance so the caller's fast path
    /// is a null check rather than a document-length scan producing a copy of its input.
    /// </summary>
    public static CodeLines? Analyze(
        IReadOnlyList<string> lines,
        SourceLanguage language,
        CodeComparisonOptions options)
    {
        if (language == SourceLanguage.None || !options.Any || lines.Count == 0)
        {
            return null;
        }

        var scanned = SourceScanner.Scan(lines, language);

        var comparisonLines = new string[lines.Count];
        var ignorable = new bool[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            var line = scanned[i];
            var codeOnly = options.IgnoreComments ? WithoutComments(line) : line.Text;

            comparisonLines[i] = codeOnly;
            ignorable[i] = IsIgnorable(line, codeOnly, options);
        }

        return new CodeLines(comparisonLines, ignorable);
    }

    /// <summary>
    /// What each line should be matched on - the line itself, or the line with its comments removed.
    /// Always the same length as the document, and positionally aligned with it.
    /// </summary>
    public IReadOnlyList<string> ComparisonLines { get; }

    /// <summary>
    /// Whether the line at <paramref name="lineNumber"/> (1-based, matching <c>DiffLine.LeftNumber</c>)
    /// contributes nothing the user asked to see - blank when blank lines are ignored, comment-only
    /// when comments are.
    /// </summary>
    public bool IsIgnorable(int lineNumber) =>
        lineNumber >= 1 && lineNumber <= _ignorable.Count && _ignorable[lineNumber - 1];

    private static bool IsIgnorable(SourceLineTokens line, string codeOnly, CodeComparisonOptions options)
    {
        var blank = string.IsNullOrWhiteSpace(line.Text);

        if (blank)
        {
            return options.IgnoreBlankLines;
        }

        // Comment-only is decided from the STRIPPED text rather than by inspecting token kinds, so the
        // two can never disagree: if what is left after stripping is nothing, the line was nothing but
        // a comment, by construction.
        return options.IgnoreComments && string.IsNullOrWhiteSpace(codeOnly);
    }

    /// <summary>
    /// The line with its comments removed.
    ///
    /// Whitespace immediately BEFORE a comment goes with it, which is what makes the result stable:
    /// <c>foo(); // note</c> has to reduce to exactly <c>foo();</c>, not <c>foo(); </c>, or it would
    /// still differ from a line that never had the comment. The same rule handles an interior comment -
    /// <c>f(a /* x */, b)</c> reduces to <c>f(a, b)</c>, which is what the reader means by "ignore the
    /// comment".
    /// </summary>
    private static string WithoutComments(SourceLineTokens line)
    {
        var hasComment = false;
        foreach (var token in line.Tokens)
        {
            if (token.Kind == SourceTokenKind.Comment)
            {
                hasComment = true;
                break;
            }
        }

        if (!hasComment)
        {
            // The overwhelmingly common case. Returning the original string avoids a copy per line of
            // every code file compared with the option on.
            return line.Text;
        }

        var builder = new StringBuilder(line.Text.Length);
        var pendingWhitespace = 0;

        foreach (var token in line.Tokens)
        {
            if (token.Kind == SourceTokenKind.Comment)
            {
                // Drop the run of whitespace that led into it as well.
                builder.Length -= pendingWhitespace;
                pendingWhitespace = 0;
                continue;
            }

            builder.Append(line.Text, token.Start, token.Length);
            pendingWhitespace = token.Kind == SourceTokenKind.Whitespace ? token.Length : 0;
        }

        return builder.ToString();
    }
}
