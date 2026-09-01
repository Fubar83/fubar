using Fubar.Diff.Core.Languages;

namespace Fubar.Diff.Core.Code;

/// <summary>
/// PORT. Turns source text into the structure tree <see cref="CodeStructureDiffer"/> compares.
///
/// A port for the same reason <see cref="Json.IJsonParser"/> is one: the only credible implementation
/// is a compiler front end, which is a large external dependency, and Core must not take one. It also
/// keeps the door open for a second language without the differ, the tree or the UI learning anything
/// - <see cref="CanParse"/> is the whole of what they need to ask.
/// </summary>
public interface ICodeStructureParser
{
    /// <summary>Whether this parser can read that language at all.</summary>
    bool CanParse(SourceLanguage language);

    /// <summary>
    /// Parses source text into a structure tree.
    ///
    /// Returns false rather than throwing when the text will not parse, because a file that does not
    /// compile is a completely ordinary thing to be diffing - mid-edit, mid-merge, mid-conflict - and
    /// is arguably when a diff is wanted most. The caller falls back to the plain text comparison,
    /// which was never in any doubt.
    /// </summary>
    bool TryParse(string text, SourceLanguage language, out CodeNode? root);
}
