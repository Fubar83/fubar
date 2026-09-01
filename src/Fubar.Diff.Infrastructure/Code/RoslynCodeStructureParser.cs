using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fubar.Diff.Core.Code;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Languages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Fubar.Diff.Infrastructure.Code;

/// <summary>
/// ADAPTER. Builds a <see cref="CodeNode"/> tree from a C# file, using Roslyn's syntax parser.
///
/// Syntax only: <see cref="CSharpSyntaxTree.ParseText(string, CSharpParseOptions, string, Encoding, System.Threading.CancellationToken)"/>
/// and nothing above it. Fubar Diff is handed two files off a disk with no project, no references and
/// no build around them, so a semantic model is not merely expensive here - it is not available, and
/// pretending otherwise would mean a structural comparison that only worked inside a solution. Syntax
/// answers every question this feature asks: what members are there, what are they called, what do
/// they contain, and where are they.
///
/// Parsing never fails. Roslyn's parser is error-tolerant by design - it recovers and produces a tree
/// with diagnostics rather than throwing - which is exactly the behaviour wanted, because a file that
/// does not compile is a completely normal thing to be diffing. A file so broken that the tree holds
/// nothing is reported as unparsed via <see cref="TryParse"/> returning false, and the caller falls
/// back to the text comparison that was never in doubt.
/// </summary>
public sealed class RoslynCodeStructureParser : ICodeStructureParser
{
    /// <summary>
    /// Parsed at the latest language version the referenced Roslyn knows, deliberately.
    ///
    /// A file using a construct newer than the parser still parses - the recovery produces a tree with
    /// the rest of the file intact - but a member built out of the syntax it did not understand comes
    /// out mis-shaped, and a mis-shaped member is worse than a missing one because it will pair with
    /// something. Taking the newest available is the cheapest way to keep that rare.
    /// </summary>
    private static readonly CSharpParseOptions Options =
        new(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

    /// <summary>
    /// What goes between two tokens in <see cref="CodeNode.Tokens"/>.
    ///
    /// A character no source file contains, rather than a space: joining on a space would make the two
    /// tokens <c>a b</c> and the single identifier <c>ab</c> the same string, and a comparison that
    /// cannot tell those apart is worse than no comparison at all.
    /// </summary>
    private const char TokenSeparator = '\u0001';

    public bool CanParse(SourceLanguage language) => language == SourceLanguage.CSharp;

    public bool TryParse(string text, SourceLanguage language, out CodeNode? root)
    {
        root = null;

        if (!CanParse(language) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var tree = CSharpSyntaxTree.ParseText(text, Options);

        if (tree.GetRoot() is not CompilationUnitSyntax unit)
        {
            return false;
        }

        var children = new List<CodeNode>();

        foreach (var use in unit.Usings)
        {
            children.Add(Build(use, CodeMemberKind.Import, UsingName(use), UsingName(use), []));
        }

        foreach (var member in unit.Members)
        {
            children.AddRange(BuildMember(member));
        }

        // A file the parser could make nothing of - not one broken member, but nothing at all - is
        // reported as unparsed. There is no structure to compare, and an empty tree against an empty
        // tree would report "no functional changes" about two files that differ, which is the one
        // answer this feature must never give wrongly.
        if (children.Count == 0)
        {
            return false;
        }

        root = new CodeNode(CodeMemberKind.File, string.Empty, string.Empty, SpanOf(unit), children);

        return true;
    }

    /// <summary>
    /// One declaration, plus its children. Returns a sequence because a single field declaration can
    /// declare several fields - <c>int a, b;</c> - and each is its own member as far as anything
    /// reading this tree is concerned.
    /// </summary>
    private static IEnumerable<CodeNode> BuildMember(MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case BaseNamespaceDeclarationSyntax ns:
            {
                var name = ns.Name.ToString();

                yield return Build(ns, CodeMemberKind.Namespace, name, name, Children(ns.Members, ns.Usings));

                break;
            }

            case TypeDeclarationSyntax type:
            {
                var name = type.Identifier.Text + TypeParameters(type.TypeParameterList);

                yield return Build(type, KindOf(type), name, name, Children(type.Members));

                break;
            }

            case EnumDeclarationSyntax e:
            {
                var members = e.Members
                    .Select(m => Build(m, CodeMemberKind.EnumMember, m.Identifier.Text, m.Identifier.Text, []))
                    .ToList();

                yield return Build(e, CodeMemberKind.Enum, e.Identifier.Text, e.Identifier.Text, members);

                break;
            }

            case DelegateDeclarationSyntax d:
            {
                var name = d.Identifier.Text + TypeParameters(d.TypeParameterList);

                yield return Build(d, CodeMemberKind.Delegate, name, name + Parameters(d.ParameterList), []);

                break;
            }

            case MethodDeclarationSyntax m:
            {
                var name = m.Identifier.Text + TypeParameters(m.TypeParameterList);

                yield return Build(m, CodeMemberKind.Method, name, name + Parameters(m.ParameterList), []);

                break;
            }

            case ConstructorDeclarationSyntax c:
                yield return Build(
                    c, CodeMemberKind.Constructor, c.Identifier.Text, c.Identifier.Text + Parameters(c.ParameterList), []);

                break;

            case DestructorDeclarationSyntax d:
                yield return Build(d, CodeMemberKind.Destructor, "~" + d.Identifier.Text, "~" + d.Identifier.Text, []);

                break;

            case OperatorDeclarationSyntax o:
            {
                var name = "operator " + o.OperatorToken.Text;

                yield return Build(o, CodeMemberKind.Operator, name, name + Parameters(o.ParameterList), []);

                break;
            }

            case ConversionOperatorDeclarationSyntax o:
            {
                var name = o.ImplicitOrExplicitKeyword.Text + " operator " + o.Type;

                yield return Build(o, CodeMemberKind.Operator, name, name + Parameters(o.ParameterList), []);

                break;
            }

            case PropertyDeclarationSyntax p:
                yield return Build(p, CodeMemberKind.Property, p.Identifier.Text, p.Identifier.Text, []);

                break;

            case IndexerDeclarationSyntax i:
                yield return Build(i, CodeMemberKind.Indexer, "this[]", "this" + Parameters(i.ParameterList), []);

                break;

            case EventDeclarationSyntax e:
                yield return Build(e, CodeMemberKind.Event, e.Identifier.Text, e.Identifier.Text, []);

                break;

            case EventFieldDeclarationSyntax e:
            {
                foreach (var declared in e.Declaration.Variables)
                {
                    yield return Build(
                        e, CodeMemberKind.Event, declared.Identifier.Text, declared.Identifier.Text, [], declared);
                }

                break;
            }

            case FieldDeclarationSyntax f:
            {
                // One node per declarator, so `int a, b;` reports as two fields. The SPAN stays the
                // whole declaration, because that is what the reader has to look at either way, and
                // pointing at half a statement helps nobody.
                foreach (var declared in f.Declaration.Variables)
                {
                    yield return Build(
                        f, CodeMemberKind.Field, declared.Identifier.Text, declared.Identifier.Text, [], declared);
                }

                break;
            }

            case GlobalStatementSyntax:
                // Top-level statements. Deliberately not given a node: they have no name to match on,
                // so every one of them would pair by position and report as changed the moment one was
                // inserted - which is the line differ's answer, arrived at expensively.
                break;

            default:
                yield return Build(member, CodeMemberKind.Other, member.Kind().ToString(), member.Kind().ToString(), []);

                break;
        }
    }

    private static List<CodeNode> Children(
        SyntaxList<MemberDeclarationSyntax> members,
        SyntaxList<UsingDirectiveSyntax> usings = default)
    {
        var children = new List<CodeNode>();

        foreach (var use in usings)
        {
            children.Add(Build(use, CodeMemberKind.Import, UsingName(use), UsingName(use), []));
        }

        foreach (var member in members)
        {
            children.AddRange(BuildMember(member));
        }

        return children;
    }

    /// <summary>
    /// Builds one node, working out its own text and tokens - "own" meaning everything in the
    /// declaration that does not belong to one of its <paramref name="children"/>.
    ///
    /// That subtraction is the whole reason a class does not report as changed whenever a method
    /// inside it does. The alternative - hashing each node's full text - would make every ancestor of
    /// every edit a change, and the tree would say "the file changed, the class changed, the method
    /// changed" where only the last of those is information.
    /// </summary>
    private static CodeNode Build(
        SyntaxNode syntax,
        CodeMemberKind kind,
        string name,
        string signature,
        List<CodeNode> children,
        VariableDeclaratorSyntax? declarator = null)
    {
        var (tokens, text) = OwnText(syntax, children);

        return new CodeNode(kind, name, signature, SpanOf(declarator ?? syntax), children)
        {
            Tokens = tokens,
            Text = text,
            BodyTokens = BodyTokensOf(syntax),
            Header = HeaderOf(syntax),
        };
    }

    /// <summary>
    /// The node's own tokens, twice: once as a token sequence with formatting gone, and once as the
    /// text they were written as.
    ///
    /// See <see cref="TokenSeparator"/> for why the token sequence is not simply space-separated.
    ///
    /// The text half has one rule that took a failing test to find, and without it the whole feature
    /// is noise: the BLANK SPACE AROUND a member does not belong to it. Insert a method above another
    /// and the one below gains a line break it did not have; delete the last class in a namespace and
    /// the namespace loses one. Neither member was touched, and reporting both as reformatted buries
    /// the single change that was real under two that were not. So whitespace at the very start and
    /// the very end of the node is dropped, while everything BETWEEN its own tokens is kept - which is
    /// exactly where re-indentation and rewrapping live, the changes this is here to catch. Comments
    /// are kept at the edges too, so an added or edited doc comment still reports.
    /// </summary>
    private static (string Tokens, string Text) OwnText(SyntaxNode syntax, List<CodeNode> children)
    {
        var tokens = new StringBuilder();
        var text = new StringBuilder();
        var own = OwnTokens(syntax, children.Count > 0);

        for (var i = 0; i < own.Count; i++)
        {
            var token = own[i];

            tokens.Append(token.Text).Append(TokenSeparator);

            Append(text, token.LeadingTrivia, dropWhitespace: i == 0);
            text.Append(token.Text);
            Append(text, token.TrailingTrivia, dropWhitespace: i == own.Count - 1);
        }

        // Line endings only. Everything else about the whitespace is kept, because "someone rewrapped
        // this" is a genuine cosmetic change worth reporting - whereas CRLF against LF is a whole-file
        // fact that TextFormatDifference already states once, and restating it once per member would
        // bury every real answer under it.
        return (tokens.ToString(), text.Replace("\r\n", "\n").ToString());
    }

    private static void Append(StringBuilder text, SyntaxTriviaList trivia, bool dropWhitespace)
    {
        foreach (var piece in trivia)
        {
            if (dropWhitespace && piece.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                continue;
            }

            if (dropWhitespace && piece.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                continue;
            }

            text.Append(piece.ToFullString());
        }
    }

    /// <summary>
    /// The tokens that belong to this node and not to a nested one, in order.
    ///
    /// Written as a walk that does not DESCEND into nested members rather than as a filter over
    /// <c>DescendantTokens()</c>, and the difference is not stylistic. Filtering enumerates every
    /// token under the node - so a namespace enumerates the whole file, a class enumerates every
    /// method in it - once per level of nesting, and then tests each against a list of spans. On a
    /// 2 MB file that measured 1.3 seconds; walking only what the node owns is a few milliseconds,
    /// because the total work is then the size of the file rather than the size of the file times
    /// its depth.
    ///
    /// Nested members are always DIRECT children of the node they belong to, in every shape this
    /// parser builds, so one check at each level is enough.
    /// </summary>
    private static List<SyntaxToken> OwnTokens(SyntaxNode syntax, bool hasChildren)
    {
        var own = new List<SyntaxToken>();

        Walk(syntax, top: true);

        return own;

        void Walk(SyntaxNode node, bool top)
        {
            foreach (var child in node.ChildNodesAndTokens())
            {
                if (child.AsNode() is not { } inner)
                {
                    own.Add(child.AsToken());

                    continue;
                }

                if (top && hasChildren && inner is MemberDeclarationSyntax or UsingDirectiveSyntax)
                {
                    continue;
                }

                Walk(inner, top: false);
            }
        }
    }

    /// <summary>
    /// The tokens of the member's BODY - what it does, with its name and signature left out.
    ///
    /// A method matched to another by an identical one of these has been renamed and nothing more,
    /// which is the single fact that separates a rename from a member vanishing and an unrelated one
    /// appearing. Null-bodied members (an abstract method, a field with no initializer) return empty,
    /// which the differ reads as "not eligible" rather than as "matches everything else with no body".
    /// </summary>
    private static string BodyTokensOf(SyntaxNode syntax)
    {
        SyntaxNode? body = syntax switch
        {
            MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
            ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody,
            DestructorDeclarationSyntax d => (SyntaxNode?)d.Body ?? d.ExpressionBody,
            OperatorDeclarationSyntax o => (SyntaxNode?)o.Body ?? o.ExpressionBody,
            ConversionOperatorDeclarationSyntax o => (SyntaxNode?)o.Body ?? o.ExpressionBody,
            PropertyDeclarationSyntax p => (SyntaxNode?)p.AccessorList ?? p.ExpressionBody,
            IndexerDeclarationSyntax i => (SyntaxNode?)i.AccessorList ?? i.ExpressionBody,
            EventDeclarationSyntax e => e.AccessorList,
            FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Initializer,
            _ => null,
        };

        if (body is null)
        {
            return string.Empty;
        }

        var tokens = new StringBuilder();

        foreach (var token in body.DescendantTokens())
        {
            tokens.Append(token.Text).Append(TokenSeparator);
        }

        return tokens.ToString();
    }

    /// <summary>The declaration line as a person would read it, for the tree. Never used for matching.</summary>
    private static string HeaderOf(SyntaxNode syntax)
    {
        var header = syntax switch
        {
            MethodDeclarationSyntax m => $"{m.Modifiers} {m.ReturnType} {m.Identifier}{m.TypeParameterList}{m.ParameterList}",
            ConstructorDeclarationSyntax c => $"{c.Modifiers} {c.Identifier}{c.ParameterList}",
            OperatorDeclarationSyntax o => $"{o.Modifiers} {o.ReturnType} operator {o.OperatorToken}{o.ParameterList}",
            PropertyDeclarationSyntax p => $"{p.Modifiers} {p.Type} {p.Identifier}",
            IndexerDeclarationSyntax i => $"{i.Modifiers} {i.Type} this{i.ParameterList}",
            EventDeclarationSyntax e => $"{e.Modifiers} event {e.Type} {e.Identifier}",
            FieldDeclarationSyntax f => $"{f.Modifiers} {f.Declaration.Type}",
            TypeDeclarationSyntax t => $"{t.Modifiers} {t.Keyword} {t.Identifier}{t.TypeParameterList}",
            EnumDeclarationSyntax e => $"{e.Modifiers} enum {e.Identifier}",
            DelegateDeclarationSyntax d => $"{d.Modifiers} delegate {d.ReturnType} {d.Identifier}{d.ParameterList}",
            BaseNamespaceDeclarationSyntax n => $"namespace {n.Name}",
            UsingDirectiveSyntax u => u.ToString().Trim(),
            _ => string.Empty,
        };

        return string.Join(' ', header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static CodeMemberKind KindOf(TypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax => CodeMemberKind.Record,
        StructDeclarationSyntax => CodeMemberKind.Struct,
        InterfaceDeclarationSyntax => CodeMemberKind.Interface,
        ClassDeclarationSyntax => CodeMemberKind.Class,
        _ => CodeMemberKind.Other,
    };

    private static string UsingName(UsingDirectiveSyntax use) =>
        (use.Alias is not null ? use.Alias.Name + " = " : string.Empty) + use.NamespaceOrType;

    private static string TypeParameters(TypeParameterListSyntax? list) =>
        list is null ? string.Empty : $"<{string.Join(", ", list.Parameters.Select(p => p.Identifier.Text))}>";

    /// <summary>
    /// A parameter list reduced to its TYPES, which is what makes two overloads different members and
    /// a renamed parameter the same one. Renaming a parameter is a change to the member, and it is
    /// reported as one - by the tokens - rather than by the two overloads failing to pair up.
    /// </summary>
    private static string Parameters(BaseParameterListSyntax? list) =>
        list is null
            ? "()"
            : $"({string.Join(", ", list.Parameters.Select(p => p.Type?.ToString() ?? "?"))})";

    /// <summary>
    /// Roslyn positions are 0-based; <see cref="SourceSpan"/> is 1-based throughout this codebase.
    /// Converted here, once, at the boundary.
    /// </summary>
    private static SourceSpan SpanOf(SyntaxNode syntax)
    {
        var span = syntax.SyntaxTree.GetLineSpan(syntax.Span);

        return new SourceSpan(
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1);
    }
}
