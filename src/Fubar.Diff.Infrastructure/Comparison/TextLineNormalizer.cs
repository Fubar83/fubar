using System;
using System.Collections.Generic;
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

            var formatted = JsonSerializer.Serialize(document.RootElement, CanonicalJson);
            result = formatted.Split('\n');
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
            result = xml.ToString(SaveOptions.None).Split('\n');
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Indented output so structure shows up as line-level diffs rather than one enormous line.
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = true,
    };
}
