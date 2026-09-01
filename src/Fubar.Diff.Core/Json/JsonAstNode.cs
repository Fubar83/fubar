using System.Collections.Generic;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// A parsed JSON value, keeping the source location that <c>System.Text.Json</c> discards.
///
/// Named <c>JsonAst*</c> rather than the obvious <c>JsonNode</c> etc. to avoid colliding with
/// <c>System.Text.Json.Nodes</c>, which any file doing JSON work is likely to have in scope.
/// </summary>
public abstract class JsonAstNode
{
    protected JsonAstNode(SourceSpan span) => Span = span;

    /// <summary>Where this value appears in the original text.</summary>
    public SourceSpan Span { get; }

    /// <summary>The node kind, for switching without a type test.</summary>
    public abstract JsonAstKind Kind { get; }
}

/// <summary>The kinds of value a JSON document can hold.</summary>
public enum JsonAstKind
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Null,
}

/// <summary>
/// A JSON object. Properties keep their source order, because the parser cannot know whether the
/// caller cares about it - <see cref="JsonComparisonOptions.ReportPropertyOrder"/> decides that later.
/// </summary>
public sealed class JsonAstObject : JsonAstNode
{
    public JsonAstObject(IReadOnlyList<JsonAstProperty> properties, SourceSpan span)
        : base(span) => Properties = properties;

    public override JsonAstKind Kind => JsonAstKind.Object;

    /// <summary>The properties, in the order they appeared.</summary>
    public IReadOnlyList<JsonAstProperty> Properties { get; }

    /// <summary>
    /// Properties above which a lookup builds an index instead of scanning.
    ///
    /// Every caller of <see cref="Find"/> is inside a loop over the OTHER document's properties, so a
    /// linear scan makes those loops quadratic. That is invisible on the objects JSON usually holds -
    /// a handful of keys, where scanning beats allocating a dictionary - and ruinous on the ones it
    /// sometimes holds: a minified document of 120,000 properties spent 45 SECONDS in
    /// <c>ArrayKeyScanner</c>, which was looking for arrays it never found.
    /// </summary>
    private const int IndexFrom = 16;

    /// <summary>
    /// Built on the first lookup of a large object and kept - the node is immutable, so it can never
    /// go stale. Two threads racing to build it both produce the same map and one wins; nothing
    /// observes a half-built one, because each builds its own before assigning.
    /// </summary>
    private Dictionary<string, JsonAstProperty>? _byName;

    /// <summary>
    /// Looks up a property by name. Returns the FIRST match: duplicate names are legal JSON but
    /// ill-defined, and every mainstream parser resolves them one way or the other rather than
    /// failing.
    /// </summary>
    public JsonAstProperty? Find(string name)
    {
        if (Properties.Count >= IndexFrom)
        {
            _byName ??= BuildIndex();

            return _byName.TryGetValue(name, out var indexed) ? indexed : null;
        }

        foreach (var property in Properties)
        {
            if (string.Equals(property.Name, name, System.StringComparison.Ordinal))
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>TryAdd, so a duplicate name resolves to the first one exactly as the scan above does.</summary>
    private Dictionary<string, JsonAstProperty> BuildIndex()
    {
        var index = new Dictionary<string, JsonAstProperty>(Properties.Count, System.StringComparer.Ordinal);

        foreach (var property in Properties)
        {
            index.TryAdd(property.Name, property);
        }

        return index;
    }
}

/// <summary>
/// One <c>"name": value</c> pair.
/// </summary>
/// <param name="Name">The property name, unescaped.</param>
/// <param name="Value">Its value.</param>
/// <param name="NameSpan">
/// Where the name itself sits, separately from <see cref="JsonAstNode.Span"/> on the value - so a
/// renamed key can be highlighted without also highlighting an unchanged value.
/// </param>
public sealed record JsonAstProperty(string Name, JsonAstNode Value, SourceSpan NameSpan);

/// <summary>A JSON array.</summary>
public sealed class JsonAstArray : JsonAstNode
{
    public JsonAstArray(IReadOnlyList<JsonAstNode> items, SourceSpan span)
        : base(span) => Items = items;

    public override JsonAstKind Kind => JsonAstKind.Array;

    public IReadOnlyList<JsonAstNode> Items { get; }
}

/// <summary>
/// A string, number, boolean, or null.
/// </summary>
public sealed class JsonAstScalar : JsonAstNode
{
    public JsonAstScalar(JsonAstKind kind, string rawText, string? value, SourceSpan span)
        : base(span)
    {
        Kind = kind;
        RawText = rawText;
        Value = value;
    }

    public override JsonAstKind Kind { get; }

    /// <summary>
    /// Exactly as written, including quotes and escapes. Kept so the tree view can show a number the
    /// way the author wrote it - <c>1.0</c> and <c>1.00</c> are the same value but not the same text,
    /// and silently reformatting someone's file in a diff view is unhelpful.
    /// </summary>
    public string RawText { get; }

    /// <summary>
    /// The unescaped string value, or null for non-strings. Comparison uses <see cref="RawText"/> for
    /// numbers so that <c>1.0</c> and <c>1</c> are reported as different, which is what a diff of a
    /// text file should say.
    /// </summary>
    public string? Value { get; }

    /// <summary>The text two scalars are compared by.</summary>
    public string ComparisonText => Kind == JsonAstKind.String ? Value ?? string.Empty : RawText;
}
