namespace Fubar.Diff.Core.Json;

/// <summary>
/// PORT. Parses YAML into the same AST as <see cref="IJsonParser"/>.
///
/// The same tree on purpose, and it is the whole reason semantic YAML cost so little: everything
/// downstream of the parser - the differ, ignore rules, array identity keys, the change tree, the
/// spans that highlight a change in the text, navigation, the reports - works on
/// <see cref="JsonAstNode"/> and does not care where it came from. YAML's data model IS this model:
/// mappings, sequences and scalars.
///
/// What does NOT survive the mapping is worth knowing: comments and formatting. A YAML parser reports
/// values, so a comparison of two files differing only in comments has nothing to report, and Text
/// mode is the answer there. Anchors and aliases are resolved, so two documents that spell the same
/// value differently compare as equal - which is right, and is the same promise made for a JSON
/// document written with different whitespace.
///
/// A separate port rather than a second implementation of <see cref="IJsonParser"/> because the two
/// are chosen differently, and that difference matters: JSON can be recognised by trying to parse it,
/// since almost nothing else is valid JSON. YAML cannot - a plain sentence is a valid YAML document -
/// so it is chosen by file extension and never guessed at.
/// </summary>
public interface IYamlParser
{
    /// <summary>
    /// Parses a YAML document, returning false rather than throwing when the text is not valid.
    ///
    /// Only the Try form: unlike JSON, nothing here ever parses YAML on the assumption that it will
    /// work - the caller has already decided from the file's name that YAML is what this should be,
    /// and a file that does not parse falls back to a text comparison.
    /// </summary>
    bool TryParse(string text, out JsonAstNode? node, out JsonParseException? error);
}
