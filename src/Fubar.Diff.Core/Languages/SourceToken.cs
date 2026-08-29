using System.Collections.Generic;

namespace Fubar.Diff.Core.Languages;

/// <summary>What a run of characters is, as far as the comparison needs to care.</summary>
public enum SourceTokenKind
{
    /// <summary>Spaces and tabs between other tokens.</summary>
    Whitespace,

    /// <summary>A line or block comment, including its delimiters.</summary>
    Comment,

    /// <summary>A string or character literal, including its quotes.</summary>
    String,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>An identifier or keyword. The two are not distinguished - nothing here needs to.</summary>
    Identifier,

    /// <summary>Punctuation or an operator. Multi-character operators are ONE token - see <see cref="SourceScanner"/>.</summary>
    Operator,
}

/// <summary>
/// One token, as an offset into the line it came from rather than a substring.
///
/// Offsets rather than text on purpose: every consumer here (inline diff spans, comment stripping)
/// needs to map back to positions in the display line anyway, and a document's worth of substrings is
/// a lot of garbage to produce for information the caller already holds.
/// </summary>
/// <param name="Start">0-based offset into the line.</param>
/// <param name="Length">Length in characters. Never zero.</param>
/// <param name="Kind">What this run is.</param>
public readonly record struct SourceToken(int Start, int Length, SourceTokenKind Kind)
{
    /// <summary>One past the last character.</summary>
    public int End => Start + Length;

    /// <summary>This token's text, cut from the line it was scanned from.</summary>
    public string TextIn(string line) => line.Substring(Start, Length);
}

/// <summary>
/// One line's tokens, alongside the text they address. Tokens tile the line completely and in order -
/// their lengths sum to the line's length - which is what lets a caller reconstruct the line, or any
/// filtered version of it, without consulting the scanner again.
/// </summary>
/// <param name="Text">The line as given.</param>
/// <param name="Tokens">Its tokens, in order, covering every character.</param>
public sealed record SourceLineTokens(string Text, IReadOnlyList<SourceToken> Tokens);
