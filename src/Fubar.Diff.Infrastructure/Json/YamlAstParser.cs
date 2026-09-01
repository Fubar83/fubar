using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Fubar.Diff.Core.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Fubar.Diff.Infrastructure.Json;

/// <summary>
/// <see cref="IYamlParser"/> over YamlDotNet's representation model, mapped onto the same AST the
/// JSON parser produces.
///
/// The representation model is used rather than deserialisation because it keeps what this needs and
/// a deserialiser throws away: every node carries the line and column it started and ended at, which
/// is what lets a structural difference be highlighted in the text the user is looking at.
/// </summary>
public sealed class YamlAstParser : IYamlParser
{
    public bool TryParse(string text, out JsonAstNode? node, out JsonParseException? error)
    {
        node = null;
        error = null;

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));

            node = FromDocuments(stream.Documents);

            if (node is null)
            {
                error = new JsonParseException("the document is empty.", SourceSpan.None);

                return false;
            }

            return true;
        }
        catch (YamlException failure)
        {
            error = new JsonParseException(
                Reason(failure),
                Span(failure.Start, failure.End));

            return false;
        }
    }

    /// <summary>
    /// One document becomes itself; several become an array of documents.
    ///
    /// Multi-document files are ordinary in the places YAML is used most - a Kubernetes manifest is
    /// routinely a Deployment, a Service and a ConfigMap separated by <c>---</c> - and comparing only
    /// the first would quietly ignore most of the file. As an array they compare element by element,
    /// and the array identity keys that already exist can even key them by name.
    /// </summary>
    private static JsonAstNode? FromDocuments(IList<YamlDocument> documents)
    {
        if (documents.Count == 0)
        {
            return null;
        }

        if (documents.Count == 1)
        {
            return From(documents[0].RootNode);
        }

        var items = new List<JsonAstNode>(documents.Count);
        foreach (var document in documents)
        {
            items.Add(From(document.RootNode));
        }

        return new JsonAstArray(items, Span(documents[0].RootNode.Start, documents[^1].RootNode.End));
    }

    private static JsonAstNode From(YamlNode node) => node switch
    {
        YamlMappingNode mapping => FromMapping(mapping),
        YamlSequenceNode sequence => FromSequence(sequence),
        YamlScalarNode scalar => FromScalar(scalar),

        // An alias to a node the loader could not resolve, which YamlDotNet reports as its own kind.
        // Treated as an empty value rather than refused: one unresolvable reference should not cost
        // the user a comparison of everything else in the file.
        _ => new JsonAstScalar(JsonAstKind.Null, string.Empty, null, Span(node.Start, node.End)),
    };

    private static JsonAstObject FromMapping(YamlMappingNode mapping)
    {
        var properties = new List<JsonAstProperty>(mapping.Children.Count);

        foreach (var (key, value) in mapping.Children)
        {
            // A key is a node in its own right, which is what gives the property a name span - and so
            // lets an added or removed field highlight its key as well as its value, exactly as in
            // JSON. A non-scalar key (YAML allows them; almost nothing uses them) is named by its
            // text so it still has an identity to match on.
            var name = key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : key.ToString();

            properties.Add(new JsonAstProperty(name, From(value), Span(key.Start, key.End)));
        }

        return new JsonAstObject(properties, Span(mapping.Start, mapping.End));
    }

    private static JsonAstArray FromSequence(YamlSequenceNode sequence)
    {
        var items = new List<JsonAstNode>(sequence.Children.Count);

        foreach (var child in sequence.Children)
        {
            items.Add(From(child));
        }

        return new JsonAstArray(items, Span(sequence.Start, sequence.End));
    }

    /// <summary>
    /// A scalar, with the type YAML's core schema says it has.
    ///
    /// Style decides first, and that is the point of doing this by hand rather than trusting a
    /// resolver: <c>port: 8080</c> and <c>port: "8080"</c> are a number and a string, and a diff that
    /// called them equal would be hiding the change most likely to break something. Anything quoted
    /// is a string, whatever it looks like.
    /// </summary>
    private static JsonAstScalar FromScalar(YamlScalarNode scalar)
    {
        var raw = scalar.Value ?? string.Empty;
        var span = Span(scalar.Start, scalar.End);

        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted
            or ScalarStyle.Literal or ScalarStyle.Folded)
        {
            return new JsonAstScalar(JsonAstKind.String, raw, raw, span);
        }

        return new JsonAstScalar(KindOf(raw), raw, raw, span);
    }

    /// <summary>
    /// The YAML 1.2 core schema's plain scalars, and nothing beyond them.
    ///
    /// Deliberately not YAML 1.1's <c>yes</c>/<c>no</c>/<c>on</c>/<c>off</c> booleans. That rule is
    /// why <c>country: NO</c> famously becomes <c>false</c>, and a diff tool inventing that reading
    /// would be reporting a change of type nobody wrote. Anything not recognised here is a string,
    /// which is also what it is to anything reading the file with a modern parser.
    /// </summary>
    private static JsonAstKind KindOf(string raw)
    {
        if (raw.Length == 0 || raw == "~" || raw is "null" or "Null" or "NULL")
        {
            return JsonAstKind.Null;
        }

        if (raw is "true" or "True" or "TRUE" or "false" or "False" or "FALSE")
        {
            return JsonAstKind.Boolean;
        }

        return IsNumber(raw) ? JsonAstKind.Number : JsonAstKind.String;
    }

    private static bool IsNumber(string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
        || (raw.StartsWith("0x", StringComparison.Ordinal)
            && long.TryParse(raw.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _));

    /// <summary>
    /// YamlDotNet's marks into a <see cref="SourceSpan"/>. Both are 1-based line and column, and both
    /// end just past the node, so this is a straight translation.
    /// </summary>
    private static SourceSpan Span(Mark start, Mark end) =>
        new((int)start.Line, (int)start.Column, (int)end.Line, (int)end.Column);

    /// <summary>
    /// YamlException messages already read as prose ("While parsing a block mapping, did not find
    /// expected key"), so the position is stripped rather than the message rewritten - the span
    /// carries it, and the UI prints both.
    /// </summary>
    private static string Reason(YamlException failure)
    {
        var message = failure.Message;
        var marker = message.LastIndexOf(" at ", StringComparison.Ordinal);

        return (marker > 0 ? message[..marker] : message).TrimEnd('.') + ".";
    }
}
