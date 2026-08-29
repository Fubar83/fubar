using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Infrastructure.Comparison;

/// <summary>
/// <see cref="ILineNormalizer"/> for text, JSON and XML.
///
/// Canonicalisation is best-effort by design: if the content does not parse, the lines come back
/// untouched and the comparison degrades to a plain text diff. A malformed file is exactly when a
/// user most wants to see a diff, so failing to parse must never fail the comparison.
/// </summary>
public sealed class TextLineNormalizer : ILineNormalizer
{
    public string ToComparisonKey(string line, ComparisonOptions options)
    {
        var key = line;

        if (options.IgnoreWhitespace)
        {
            key = key.Trim();
        }

        if (options.IgnoreCase)
        {
            // Invariant, not current-culture: a diff must not change its answer because of the
            // machine's locale (the Turkish dotless-i problem).
            key = key.ToUpperInvariant();
        }

        if (options.NormalizeUnicode)
        {
            // Guarded by IsNormalized: the check is cheap and true for essentially all real input, so
            // the allocating path only runs for lines that genuinely need it. Form C (compose) rather
            // than D, because it is what the web, Windows and Linux already produce - normalising
            // toward the majority keeps the KEY closest to the text on screen.
            key = key.IsNormalized(NormalizationForm.FormC)
                ? key
                : key.Normalize(NormalizationForm.FormC);
        }

        return key;
    }

    public IReadOnlyList<string> Canonicalize(IReadOnlyList<string> lines, ComparisonOptions options)
    {
        if (!options.NormalizeStructure || lines.Count == 0)
        {
            return lines;
        }

        var text = string.Join('\n', lines);

        return TryCanonicalizeJson(text, out var json) ? json
            : TryCanonicalizeXml(text, out var xml) ? xml
            : lines;
    }

    private static bool TryCanonicalizeJson(string text, out IReadOnlyList<string> result)
    {
        result = [];

        // Cheap pre-check: skip the parse attempt (and its exception) for content that plainly is not
        // JSON, which is the common case when this option is left on for text files.
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            result = SplitLines(PrettyPrintJson(document.RootElement));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryCanonicalizeXml(string text, out IReadOnlyList<string> result)
    {
        result = [];

        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '<')
        {
            return false;
        }

        try
        {
            // Consistent indentation and collapsed insignificant whitespace; attribute order is left
            // alone because XML attribute order, unlike JSON key order, can be meaningful to a reader.
            var xml = XDocument.Parse(text, LoadOptions.None);
            result = SplitLines(xml.ToString(SaveOptions.None));
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Splits on any of the three terminators, not just '\n'. <see cref="PrettyPrintJson"/> only ever
    /// emits a literal '\n' itself, but <c>XDocument.ToString()</c> emits <c>Environment.NewLine</c>,
    /// which is CRLF on Windows - splitting on '\n' alone there would leave a trailing '\r' glued to
    /// every line, which the diff would then treat as a byte-for-byte difference from the same
    /// document formatted on a system where the newline was LF.
    /// </summary>
    private static string[] SplitLines(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

    /// <summary>
    /// A pretty-printer tuned for LINE-BASED DIFFING rather than general readability, which is why it
    /// is hand-written instead of just calling <c>JsonSerializer</c>'s indented writer.
    ///
    /// The difference: an object or array containing only scalars is kept on ONE line. Without that,
    /// something like <c>{"id": 1}</c> inside an array expands to three lines - <c>{</c>, the property,
    /// <c>}</c> - and an array of ten such objects becomes thirty near-identical boilerplate lines.
    /// A line-based text differ then matches those boilerplate braces to EACH OTHER across unrelated
    /// elements (they are byte-identical), scrambling a clean "one element inserted" into a handful of
    /// scattered single-line changes. Keeping simple values inline removes the boilerplate that causes
    /// the confusion, while a genuinely nested object still expands so its structure is visible.
    ///
    /// Scalars and simple containers are re-serialised through <see cref="JsonSerializer"/> rather than
    /// hand-formatted, so string escaping and number formatting stay exactly what the framework would
    /// produce - this only controls line LAYOUT, never re-deriving how a value is written.
    /// </summary>
    private static string PrettyPrintJson(JsonElement element)
    {
        var builder = new StringBuilder();
        WriteJson(builder, element, indent: 0);
        return builder.ToString();
    }

    private static void WriteJson(StringBuilder builder, JsonElement element, int indent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when HasContainerChild(element.EnumerateObject().Select(p => p.Value)):
                WriteExpanded(builder, element.EnumerateObject().Select(p => (Key: (string?)p.Name, Value: p.Value)), '{', '}', indent);
                break;

            case JsonValueKind.Array when HasContainerChild(element.EnumerateArray()):
                WriteExpanded(builder, element.EnumerateArray().Select(v => ((string?)null, v)), '[', ']', indent);
                break;

            default:
                // Everything else - a scalar, an empty container, or one holding only scalars - is
                // compact and correct via the framework's own writer.
                builder.Append(JsonSerializer.Serialize(element, CompactJson));
                break;
        }
    }

    /// <summary>Whether any child is itself an object or array - the only case worth expanding for.</summary>
    private static bool HasContainerChild(IEnumerable<JsonElement> children) =>
        children.Any(v => v.ValueKind is JsonValueKind.Object or JsonValueKind.Array);

    private static void WriteExpanded(
        StringBuilder builder,
        IEnumerable<(string? Key, JsonElement Value)> items,
        char open,
        char close,
        int indent)
    {
        var list = items.ToList();

        builder.Append(open).Append('\n');

        for (var i = 0; i < list.Count; i++)
        {
            Indent(builder, indent + 1);

            if (list[i].Key is { } key)
            {
                builder.Append(JsonSerializer.Serialize(key, CompactJson)).Append(": ");
            }

            WriteJson(builder, list[i].Value, indent + 1);
            builder.Append(i < list.Count - 1 ? ",\n" : "\n");
        }

        Indent(builder, indent);
        builder.Append(close);
    }

    private static void Indent(StringBuilder builder, int level) => builder.Append(' ', level * 2);

    /// <summary>
    /// The default encoder escapes a literal quote inside a string as a numeric <c>\uXXXX</c> escape
    /// rather than the ordinary backslash-quote - a conservative choice meant for embedding JSON
    /// inside HTML/JS, which does not apply here. Relaxed is still fully JSON-safe; it just skips that
    /// extra defensive escaping, which would otherwise make this pretty-printer's output needlessly
    /// harder to read than the source it started from.
    /// </summary>
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
