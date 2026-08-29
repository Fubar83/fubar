namespace Fubar.Diff.Core.Languages;

/// <summary>
/// A language the comparison understands the STRUCTURE of - where its comments and strings begin and
/// end, and where one token stops and the next starts.
///
/// Deliberately narrow. This is not the list of languages the panes can syntax-highlight (that is a
/// display concern, keyed by file extension in the view, and covers everything TextMate ships a grammar
/// for). It is the list the COMPARISON changes its answer for, and every member here has to be backed
/// by real scanning rules in <see cref="SourceScanner"/> - so a language is added when its rules are,
/// not before.
/// </summary>
public enum SourceLanguage
{
    /// <summary>Not a language we scan - compare as plain text.</summary>
    None,

    /// <summary>C#, including verbatim (<c>@""</c>), interpolated and raw (<c>"""</c>) strings.</summary>
    CSharp,

    /// <summary>JavaScript, including template literals.</summary>
    JavaScript,

    /// <summary>TypeScript. Lexically identical to JavaScript for our purposes - the differences are all in the grammar above the token level.</summary>
    TypeScript,
}
