using System.Collections.Generic;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Core.Code;

/// <summary>
/// One node of a source file's structure: the file, a namespace, a type, or a member.
///
/// The point of this shape is the pair of texts on it. <see cref="Tokens"/> is what the code MEANS -
/// the node's own tokens with every space and comment thrown away - and <see cref="Text"/> is how it
/// was written. A member whose Tokens match and whose Text does not changed cosmetically and nothing
/// more, which is the single most useful thing a structural comparison can say and the one thing a
/// line differ can never say on its own. It is also why both are the node's OWN tokens rather than
/// everything under it: a class whose text included its members would report as changed whenever any
/// method in it changed, and the whole value here is being able to name which one.
///
/// <see cref="Span"/> is the bridge back to the text view, exactly as it is for
/// <see cref="Json.JsonAstNode"/> - the differ works on the tree and the user is looking at lines.
/// <see cref="SourceSpan"/> is shared with the JSON side rather than duplicated: there is one answer
/// to "where in the file is this", and two types that could disagree about it would eventually
/// disagree.
/// </summary>
/// <param name="Kind">What this node is.</param>
/// <param name="Name">Its name as written - <c>Total</c>, <c>Report</c>, <c>System.Linq</c>.</param>
/// <param name="Signature">
/// What identifies it among its siblings, which for an overloaded method has to include the parameter
/// types - <c>Total(int, string)</c>. Two siblings sharing a signature would not compile, so this is a
/// key rather than a heuristic.
/// </param>
/// <param name="Span">Where the declaration sits in the file, for highlighting.</param>
/// <param name="Children">Nested nodes, in source order.</param>
public sealed record CodeNode(
    CodeMemberKind Kind,
    string Name,
    string Signature,
    SourceSpan Span,
    IReadOnlyList<CodeNode> Children)
{
    /// <summary>An empty file - what a parser returns for text with nothing in it.</summary>
    public static CodeNode Empty { get; } =
        new(CodeMemberKind.File, string.Empty, string.Empty, SourceSpan.None, []);

    /// <summary>
    /// The node's own tokens, separated so that two adjacent tokens can never join into one - what the
    /// code means, with formatting and comments gone.
    ///
    /// "Own" excludes everything belonging to a <see cref="Children"/> node, so a class holds its
    /// modifiers, name, type parameters and base list, and a method holds its signature and its body.
    /// </summary>
    public string Tokens { get; init; } = string.Empty;

    /// <summary>
    /// The same span of the file as <see cref="Tokens"/>, written exactly as it was - whitespace, doc
    /// comments and all, with line endings normalised so a CRLF/LF difference does not read as every
    /// member having been touched (<see cref="Models.TextFormatDifference"/> already reports that, and
    /// says it once instead of once per member).
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Just the BODY half of <see cref="Tokens"/> - what a method does, with its name, its modifiers
    /// and its parameter list left out. Empty for anything with no body.
    ///
    /// This is what makes rename detection possible. A renamed method differs in <see cref="Tokens"/>
    /// by construction, since the name is one of them; what says "this is the same method under a new
    /// name" is that everything after the signature is untouched.
    /// </summary>
    public string BodyTokens { get; init; } = string.Empty;

    /// <summary>
    /// The declaration without its body, for display - <c>public int Total(int seed)</c>. Never used
    /// for matching; <see cref="Signature"/> is.
    /// </summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>
    /// A path built from the ancestors' names, e.g. <c>Reporting.Report.Total(int)</c>. Set by the
    /// differ rather than the parser, since it is only meaningful once two trees are being compared.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>True when this node has no children - a method rather than a type.</summary>
    public bool IsLeaf => Children.Count == 0;

    /// <summary>Every node under this one, depth first, in source order. Excludes this node.</summary>
    public IEnumerable<CodeNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;

            foreach (var grandchild in child.Descendants())
            {
                yield return grandchild;
            }
        }
    }

    public override string ToString() => $"{Kind} {Signature}";
}
