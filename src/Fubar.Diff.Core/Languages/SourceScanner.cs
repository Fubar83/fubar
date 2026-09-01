using System;
using System.Collections.Generic;

namespace Fubar.Diff.Core.Languages;

/// <summary>Which multi-line construct a line begins inside, when it begins inside one at all.</summary>
public enum ScanMode
{
    /// <summary>Ordinary code.</summary>
    Normal,

    /// <summary>Inside a <c>/* ... */</c> comment opened on an earlier line.</summary>
    BlockComment,

    /// <summary>Inside a C# verbatim string (<c>@"..."</c>) opened on an earlier line.</summary>
    VerbatimString,

    /// <summary>Inside a JavaScript/TypeScript template literal opened on an earlier line.</summary>
    TemplateString,

    /// <summary>Inside a C# raw string literal (<c>"""..."""</c>) opened on an earlier line.</summary>
    RawString,
}

/// <summary>
/// What the scanner carries from one line to the next. A line cannot be tokenised correctly on its own:
/// <c>foo();</c> is code, unless a <c>/*</c> three lines up is still open, in which case it is a comment.
/// </summary>
/// <param name="Mode">The construct still open at the start of the line.</param>
/// <param name="Delimiter">
/// For <see cref="ScanMode.RawString"/>, how many quotes close it - a C# raw string is closed by a run
/// at least as long as the one that opened it, which is the whole point of the form.
/// </param>
/// <param name="Quote">
/// Which quote character closes a <see cref="ScanMode.RawString"/>. Python's triple-quoted strings come
/// in both, and a <c>'''</c> block is not closed by <c>"""</c>.
/// </param>
public readonly record struct ScanState(ScanMode Mode, int Delimiter, char Quote = '"')
{
    /// <summary>Ordinary code - what the first line of a document starts in.</summary>
    public static ScanState Normal { get; } = new(ScanMode.Normal, 0);
}

/// <summary>How a language treats backtick-delimited text.</summary>
internal enum BacktickKind
{
    /// <summary>Not special - an ordinary character.</summary>
    None,

    /// <summary>A template literal: spans lines, and a backslash escapes the next character (JS/TS).</summary>
    Escaped,

    /// <summary>A raw string: spans lines, and a backslash is just a backslash (Go).</summary>
    Raw,
}

/// <summary>
/// What one language does differently, as data rather than as branches through the scanner.
///
/// The scanner's shape is the same for every C-family language and most of Python; what varies is a
/// handful of yes/no questions. Expressing those as a record means adding a language is a line in
/// <see cref="SourceScanner.RulesFor"/> plus tests, rather than another special case threaded through
/// the scanning loop - which is how a lexer becomes unmaintainable.
/// </summary>
/// <param name="LineComment">The token that comments out the rest of the line.</param>
/// <param name="BlockComments">Whether <c>/* ... */</c> exists.</param>
/// <param name="SingleQuotes">Whether <c>'...'</c> is a literal (a char in C-family, a string in Python).</param>
/// <param name="Backticks">See <see cref="BacktickKind"/>.</param>
/// <param name="TripleDoubleQuotes">Whether <c>"""</c> opens a multi-line literal closed by a run at least as long.</param>
/// <param name="TripleSingleQuotes">The same for <c>'''</c> - Python only.</param>
/// <param name="StringPrefixes">
/// Characters that may appear immediately before a quote and belong to the literal: <c>@$</c> in C#,
/// the f/r/b/u string prefixes in Python. A run of these NOT followed by a quote is an identifier.
/// </param>
/// <param name="VerbatimStrings">Whether <c>@"..."</c> exists, where a doubled quote escapes a quote and there are no backslash escapes.</param>
internal readonly record struct LanguageRules(
    string LineComment,
    bool BlockComments,
    bool SingleQuotes,
    BacktickKind Backticks,
    bool TripleDoubleQuotes,
    bool TripleSingleQuotes,
    string StringPrefixes,
    bool VerbatimStrings);

/// <summary>
/// A hand-written lexer for the C family, covering exactly what a DIFF needs to know: where comments
/// are, where string literals are, and where one token ends and the next begins.
///
/// Not a parser, and deliberately not aiming to be one. Nothing downstream asks what a declaration
/// means - the comparison wants to know "is this line only a comment" and "is <c>=&gt;</c> one token or
/// two", and both questions are answered at the lexical level. That keeps this in Core (BCL only,
/// no compiler dependency), keeps it fast enough to run over both sides of every comparison, and keeps
/// C# and JS/TS on ONE code path, since their lexical grammars differ only in a handful of literal
/// forms.
///
/// Known limits, all deliberate:
/// <list type="bullet">
/// <item>A JS regular-expression literal is not recognised - <c>/</c> is scanned as an operator unless
/// it opens a comment. Telling <c>/re/</c> from division needs the grammar above this level, and
/// guessing wrong would silently turn code into a "string".</item>
/// <item><c>${...}</c> inside a template literal is part of the literal, not code. For diffing that is
/// arguably the better answer anyway: an interpolation that changed reads as a change to the string.</item>
/// <item>Keywords are not distinguished from identifiers. Nothing here needs the distinction.</item>
/// </list>
/// </summary>
public static class SourceScanner
{
    /// <summary>
    /// Tokenises a whole document, threading the multi-line state between lines. This is the entry
    /// point that gets block comments and multi-line strings RIGHT; <see cref="ScanLine(string,SourceLanguage)"/>
    /// cannot, and says so.
    /// </summary>
    public static IReadOnlyList<SourceLineTokens> Scan(IReadOnlyList<string> lines, SourceLanguage language)
    {
        var scanned = new SourceLineTokens[lines.Count];
        var state = ScanState.Normal;

        for (var i = 0; i < lines.Count; i++)
        {
            scanned[i] = new SourceLineTokens(lines[i], ScanLine(lines[i], language, state, out state));
        }

        return scanned;
    }

    /// <summary>
    /// Tokenises ONE line as if it started in ordinary code.
    ///
    /// For a line inside a block comment or a multi-line string this is wrong, and knowingly so: the
    /// inline (character-level) differ is handed two already-matched lines with no document around
    /// them, and mis-chunking the inside of a comment costs a slightly worse highlight, not a wrong
    /// diff. Anything that decides what a line MEANS must use <see cref="Scan"/> instead.
    /// </summary>
    public static IReadOnlyList<SourceToken> ScanLine(string line, SourceLanguage language) =>
        ScanLine(line, language, ScanState.Normal, out _);

    /// <summary>
    /// Tokenises one line starting from <paramref name="state"/>, reporting the state the next line
    /// starts in.
    /// </summary>
    public static IReadOnlyList<SourceToken> ScanLine(
        string line,
        SourceLanguage language,
        ScanState state,
        out ScanState next)
    {
        next = ScanState.Normal;

        if (line.Length == 0)
        {
            // An empty line closes nothing: a block comment or verbatim string spanning it is still
            // open on the line after.
            next = state;
            return [];
        }

        if (language == SourceLanguage.None)
        {
            // Nothing to scan with. One token covering the line keeps the "tokens tile the line"
            // contract, so a caller that reached here anyway degrades to whole-line granularity
            // rather than to an exception.
            return [new SourceToken(0, line.Length, SourceTokenKind.Identifier)];
        }

        var rules = RulesFor(language);
        var tokens = new List<SourceToken>();
        var i = 0;

        if (state.Mode != ScanMode.Normal)
        {
            var (end, closed) = ContinueConstruct(line, state);

            if (end > 0)
            {
                tokens.Add(new SourceToken(0, end, KindOf(state.Mode)));
            }

            if (!closed)
            {
                next = state;
                return tokens;
            }

            i = end;
        }

        while (i < line.Length)
        {
            var c = line[i];

            if (IsSpace(c))
            {
                var end = i + 1;
                while (end < line.Length && IsSpace(line[end]))
                {
                    end++;
                }

                tokens.Add(new SourceToken(i, end - i, SourceTokenKind.Whitespace));
                i = end;
                continue;
            }

            if (StartsWith(line, i, rules.LineComment))
            {
                tokens.Add(new SourceToken(i, line.Length - i, SourceTokenKind.Comment));
                return tokens;
            }

            if (rules.BlockComments && c == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                var (end, closed) = FindBlockCommentEnd(line, i + 2);
                tokens.Add(new SourceToken(i, end - i, SourceTokenKind.Comment));

                if (!closed)
                {
                    next = new ScanState(ScanMode.BlockComment, 0);
                    return tokens;
                }

                i = end;
                continue;
            }

            if (TryScanString(line, i, rules, out var stringEnd, out var carry))
            {
                tokens.Add(new SourceToken(i, stringEnd - i, SourceTokenKind.String));

                if (carry is { } open)
                {
                    next = open;
                    return tokens;
                }

                i = stringEnd;
                continue;
            }

            if (char.IsDigit(c))
            {
                var end = ScanNumber(line, i);
                tokens.Add(new SourceToken(i, end - i, SourceTokenKind.Number));
                i = end;
                continue;
            }

            if (IsIdentifierStart(c))
            {
                var end = i + 1;
                while (end < line.Length && IsIdentifierPart(line[end]))
                {
                    end++;
                }

                tokens.Add(new SourceToken(i, end - i, SourceTokenKind.Identifier));
                i = end;
                continue;
            }

            var operatorLength = OperatorLength(line, i);
            tokens.Add(new SourceToken(i, operatorLength, SourceTokenKind.Operator));
            i += operatorLength;
        }

        return tokens;
    }

    /// <summary>The token kind a carried-over construct produces for the part of the line it covers.</summary>
    private static SourceTokenKind KindOf(ScanMode mode) =>
        mode == ScanMode.BlockComment ? SourceTokenKind.Comment : SourceTokenKind.String;

    /// <summary>
    /// Finishes the construct a previous line left open, returning where it ends on this line and
    /// whether it ended here at all.
    /// </summary>
    private static (int End, bool Closed) ContinueConstruct(string line, ScanState state) => state.Mode switch
    {
        ScanMode.BlockComment => FindBlockCommentEnd(line, 0),
        ScanMode.VerbatimString => FindVerbatimEnd(line, 0),
        ScanMode.TemplateString => FindBacktickEnd(line, 0, state.Quote == 'r' ? BacktickKind.Raw : BacktickKind.Escaped),
        ScanMode.RawString => FindRawEnd(line, 0, state.Delimiter, state.Quote),
        _ => (0, true),
    };

    private static (int End, bool Closed) FindBlockCommentEnd(string line, int start)
    {
        var index = line.IndexOf("*/", start, StringComparison.Ordinal);

        return index < 0 ? (line.Length, false) : (index + 2, true);
    }

    /// <summary>
    /// Finds the end of a C# verbatim string. A doubled quote is an escaped quote and does not close
    /// it, which is the one rule that makes this different from an ordinary literal - there are no
    /// backslash escapes at all.
    /// </summary>
    private static (int End, bool Closed) FindVerbatimEnd(string line, int start)
    {
        var i = start;

        while (i < line.Length)
        {
            if (line[i] != '"')
            {
                i++;
                continue;
            }

            if (i + 1 < line.Length && line[i + 1] == '"')
            {
                i += 2;
                continue;
            }

            return (i + 1, true);
        }

        return (line.Length, false);
    }

    /// <summary>
    /// Finds the end of a backtick-delimited literal. Whether a backslash escapes the next character
    /// is the whole difference between JavaScript's template literals and Go's raw strings: in Go a
    /// trailing <c>\</c> before the closing backtick is content, and treating it as an escape would
    /// swallow the terminator and run the string on into the rest of the file.
    /// </summary>
    private static (int End, bool Closed) FindBacktickEnd(string line, int start, BacktickKind kind)
    {
        var i = start;

        while (i < line.Length)
        {
            if (kind == BacktickKind.Escaped && line[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (line[i] == '`')
            {
                return (i + 1, true);
            }

            i++;
        }

        return (line.Length, false);
    }

    /// <summary>
    /// Finds the end of a C# raw string: a run of at least <paramref name="delimiter"/> quotes. A
    /// SHORTER run is content, which is exactly why the form exists.
    /// </summary>
    private static (int End, bool Closed) FindRawEnd(string line, int start, int delimiter, char quote)
    {
        var i = start;

        while (i < line.Length)
        {
            if (line[i] != quote)
            {
                i++;
                continue;
            }

            var run = QuoteRunLength(line, i, quote);
            if (run >= delimiter)
            {
                return (i + run, true);
            }

            i += run;
        }

        return (line.Length, false);
    }

    /// <summary>
    /// Scans a string or character literal starting at <paramref name="start"/>.
    ///
    /// Returns false when the character is not a literal opener at all - notably a bare <c>@</c> or
    /// <c>$</c>, which in C# is an identifier prefix (<c>@class</c>) unless a quote follows it.
    /// </summary>
    private static bool TryScanString(
        string line,
        int start,
        LanguageRules rules,
        out int end,
        out ScanState? carry)
    {
        end = start;
        carry = null;

        // A run of prefix characters counts as part of the literal only when a quote actually follows.
        // Otherwise it is an identifier that happens to begin with those letters - Python's `buffer`
        // starts with three of its own string prefixes, and C#'s `@class` with one of its.
        var quote = start;
        var verbatim = false;

        while (quote < line.Length && rules.StringPrefixes.IndexOf(line[quote]) >= 0)
        {
            verbatim |= rules.VerbatimStrings && line[quote] == '@';
            quote++;
        }

        if (quote >= line.Length)
        {
            return false;
        }

        var opener = line[quote];
        var isDouble = opener == '"';
        var isSingle = opener == '\'' && rules.SingleQuotes;

        if (isDouble || isSingle)
        {
            var triple = isDouble ? rules.TripleDoubleQuotes : rules.TripleSingleQuotes;
            var run = QuoteRunLength(line, quote, opener);

            if (triple && run >= 3)
            {
                (end, var rawClosed) = FindRawEnd(line, quote + run, run, opener);
                carry = rawClosed ? null : new ScanState(ScanMode.RawString, run, opener);
                return true;
            }

            if (verbatim && isDouble)
            {
                (end, var verbatimClosed) = FindVerbatimEnd(line, quote + 1);
                carry = verbatimClosed ? null : new ScanState(ScanMode.VerbatimString, 0);
                return true;
            }

            end = FindQuotedEnd(line, quote + 1, opener);
            return true;
        }

        // A prefix run that led nowhere is not a literal; let the identifier scanner have it.
        if (quote != start)
        {
            return false;
        }

        if (opener == '`' && rules.Backticks != BacktickKind.None)
        {
            (end, var closed) = FindBacktickEnd(line, start + 1, rules.Backticks);
            carry = closed ? null : new ScanState(ScanMode.TemplateString, 0, GetBacktickQuote(rules.Backticks));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Remembers whether a carried-over backtick string processes escapes, since Go's does not and
    /// JavaScript's does. Stored in the state's quote slot, which a backtick string has no other use
    /// for.
    /// </summary>
    private static char GetBacktickQuote(BacktickKind kind) => kind == BacktickKind.Raw ? 'r' : '`';

    /// <summary>The scanning rules for one language. See <see cref="LanguageRules"/>.</summary>
    private static LanguageRules RulesFor(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp => new LanguageRules(
            "//", BlockComments: true, SingleQuotes: true, BacktickKind.None,
            TripleDoubleQuotes: true, TripleSingleQuotes: false, StringPrefixes: "@$", VerbatimStrings: true),

        SourceLanguage.JavaScript or SourceLanguage.TypeScript => new LanguageRules(
            "//", BlockComments: true, SingleQuotes: true, BacktickKind.Escaped,
            TripleDoubleQuotes: false, TripleSingleQuotes: false, StringPrefixes: "", VerbatimStrings: false),

        // Java text blocks use the same """ form C# raw strings do.
        SourceLanguage.Java => new LanguageRules(
            "//", BlockComments: true, SingleQuotes: true, BacktickKind.None,
            TripleDoubleQuotes: true, TripleSingleQuotes: false, StringPrefixes: "", VerbatimStrings: false),

        // Go's backtick literals are RAW: a backslash inside one is a backslash, not an escape.
        SourceLanguage.Go => new LanguageRules(
            "//", BlockComments: true, SingleQuotes: true, BacktickKind.Raw,
            TripleDoubleQuotes: false, TripleSingleQuotes: false, StringPrefixes: "", VerbatimStrings: false),

        SourceLanguage.C or SourceLanguage.Cpp => new LanguageRules(
            "//", BlockComments: true, SingleQuotes: true, BacktickKind.None,
            TripleDoubleQuotes: false, TripleSingleQuotes: false, StringPrefixes: "", VerbatimStrings: false),

        // Python has no block comments at all - a triple-quoted string used as a docstring is a
        // STRING, and treating it as a comment would let "ignore comments" delete real values.
        SourceLanguage.Python => new LanguageRules(
            "#", BlockComments: false, SingleQuotes: true, BacktickKind.None,
            TripleDoubleQuotes: true, TripleSingleQuotes: true, StringPrefixes: "fFrRbBuU", VerbatimStrings: false),

        _ => default,
    };

    private static bool StartsWith(string line, int index, string token)
    {
        if (token.Length == 0 || index + token.Length > line.Length)
        {
            return false;
        }

        return line.AsSpan(index, token.Length).SequenceEqual(token);
    }

    /// <summary>
    /// The end of an ordinary backslash-escaped literal. An unterminated one takes the rest of the
    /// line and stops there - it does NOT carry over, because this form cannot span lines, and letting
    /// it would turn one stray quote into a file-long string.
    /// </summary>
    private static int FindQuotedEnd(string line, int start, char quote)
    {
        var i = start;

        while (i < line.Length)
        {
            if (line[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (line[i] == quote)
            {
                return i + 1;
            }

            i++;
        }

        return line.Length;
    }

    private static int QuoteRunLength(string line, int start, char quote)
    {
        var end = start;
        while (end < line.Length && line[end] == quote)
        {
            end++;
        }

        return end - start;
    }

    /// <summary>
    /// Consumes a numeric literal, suffixes and separators included (<c>0xFF</c>, <c>1_000</c>,
    /// <c>1.5e-3</c>, <c>42UL</c>). A '.' only continues the number when a digit follows, so
    /// <c>1.ToString()</c> stays a number, a dot and a call rather than one strange token.
    /// </summary>
    private static int ScanNumber(string line, int start)
    {
        var i = start;

        while (i < line.Length)
        {
            var c = line[i];

            if (char.IsLetterOrDigit(c) || c == '_')
            {
                i++;
                continue;
            }

            if (c == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1]))
            {
                i++;
                continue;
            }

            if ((c == '+' || c == '-') && i > start && (line[i - 1] == 'e' || line[i - 1] == 'E'))
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    /// <summary>
    /// How many characters the operator at <paramref name="start"/> spans - the reason this scanner
    /// exists for the inline differ at all.
    ///
    /// <c>=&gt;</c>, <c>===</c> and <c>??=</c> are single tokens. Split into characters (which is what a
    /// generic punctuation chunker does) an arrow that became a lambda body highlights as two unrelated
    /// fragments, and changing <c>==</c> to <c>===</c> highlights nothing at all, because the first two
    /// characters matched.
    /// </summary>
    private static int OperatorLength(string line, int start)
    {
        foreach (var op in MultiCharOperators)
        {
            if (start + op.Length <= line.Length
                && line.AsSpan(start, op.Length).SequenceEqual(op))
            {
                return op.Length;
            }
        }

        return 1;
    }

    /// <summary>
    /// Longest first - the match above takes the first that fits, so <c>===</c> must be tried before
    /// <c>==</c> or it would never match.
    /// </summary>
    private static readonly string[] MultiCharOperators =
    [
        "===", "!==", ">>>", "...", "??=", "<<=", ">>=", "**=", "&&=", "||=",
        "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--",
        "+=", "-=", "*=", "/=", "%=", "|=", "&=", "^=", "<<", ">>", "->", "::", "**",
    ];

    private static bool IsSpace(char c) => c is ' ' or '\t' || (char.IsWhiteSpace(c) && c is not '\n' and not '\r');

    /// <summary>
    /// <c>$</c> is an identifier character in JavaScript, and <c>@</c> starts a keyword-escaped C#
    /// identifier. Reaching here with either means <see cref="TryScanString"/> already ruled out the
    /// literal forms that use them.
    /// </summary>
    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '$' or '@';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}
