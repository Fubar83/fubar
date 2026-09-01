using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Writes a parsed document back out, laid out for reading.
///
/// Works from the AST rather than from the text, which is what makes it safe: every scalar is written
/// back as its own <see cref="JsonAstScalar.RawText"/>, exactly as the author wrote it. A formatter
/// that re-derived values would quietly turn <c>1.0</c> into <c>1</c>, <c>1e3</c> into <c>1000</c> and
/// an escaped string into a differently-escaped one - and a diff tool that edits the numbers while
/// claiming to reformat them is worse than one that cannot reformat at all.
///
/// This is for DISPLAY only. The comparison is unaffected by it, and nothing formatted here is ever
/// written to a file - see the Json view's pretty toggle, which reformats one side to read it and
/// leaves the comparison saying exactly what it said before.
/// </summary>
public static class JsonFormatter
{
    /// <summary>Formats a document.</summary>
    public static string Format(JsonAstNode node, JsonFormatOptions options)
    {
        var builder = new StringBuilder();
        Write(builder, node, options, depth: 0);

        return builder.ToString();
    }

    private static void Write(StringBuilder builder, JsonAstNode node, JsonFormatOptions options, int depth)
    {
        switch (node)
        {
            case JsonAstObject obj:
                WriteObject(builder, obj, options, depth);
                break;

            case JsonAstArray array:
                WriteArray(builder, array, options, depth);
                break;

            case JsonAstScalar scalar:
                builder.Append(scalar.RawText);
                break;
        }
    }

    private static void WriteObject(StringBuilder builder, JsonAstObject obj, JsonFormatOptions options, int depth)
    {
        if (obj.Properties.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        var properties = options.SortProperties
            ? obj.Properties.OrderBy(p => p.Name, StringComparer.Ordinal).ToList()
            : (IReadOnlyList<JsonAstProperty>)obj.Properties;

        if (options.InlineSimpleContainers && !properties.Any(p => IsContainer(p.Value)))
        {
            builder.Append("{ ");

            for (var i = 0; i < properties.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                WriteName(builder, properties[i].Name, options);
                Write(builder, properties[i].Value, options, depth);
            }

            builder.Append(" }");

            return;
        }

        builder.Append("{\n");

        for (var i = 0; i < properties.Count; i++)
        {
            Indent(builder, options, depth + 1);
            WriteName(builder, properties[i].Name, options);
            Write(builder, properties[i].Value, options, depth + 1);

            builder.Append(i < properties.Count - 1 ? ",\n" : "\n");
        }

        Indent(builder, options, depth);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, JsonAstArray array, JsonFormatOptions options, int depth)
    {
        if (array.Items.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        if (options.InlineSimpleContainers && !array.Items.Any(IsContainer))
        {
            builder.Append("[ ");

            for (var i = 0; i < array.Items.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                Write(builder, array.Items[i], options, depth);
            }

            builder.Append(" ]");

            return;
        }

        builder.Append("[\n");

        for (var i = 0; i < array.Items.Count; i++)
        {
            Indent(builder, options, depth + 1);
            Write(builder, array.Items[i], options, depth + 1);

            builder.Append(i < array.Items.Count - 1 ? ",\n" : "\n");
        }

        Indent(builder, options, depth);
        builder.Append(']');
    }

    /// <summary>
    /// Writes a property name, re-escaping it the way JSON requires.
    ///
    /// The name is held unescaped on the AST - it is a lookup key, and every comparison in the app
    /// wants it that way - so unlike a scalar it cannot simply be written back verbatim.
    /// </summary>
    private static void WriteName(StringBuilder builder, string name, JsonFormatOptions options)
    {
        builder.Append('"');

        foreach (var c in name)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;

                default:
                    // Control characters have no literal form in JSON; everything else is itself.
                    if (c < 0x20)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append(options.SpaceAfterColon ? "\": " : "\":");
    }

    private static bool IsContainer(JsonAstNode node) => node is JsonAstObject or JsonAstArray;

    private static void Indent(StringBuilder builder, JsonFormatOptions options, int depth)
    {
        for (var i = 0; i < depth; i++)
        {
            builder.Append(options.IndentUnit);
        }
    }
}
