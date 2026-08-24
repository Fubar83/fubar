using System.Text;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Where a value sits in the document tree, in the usual <c>$.users[0].name</c> notation.
///
/// Immutable and built by appending, so the differ can walk down carrying one of these without any
/// backtracking bookkeeping - and the resulting string is what array-key overrides are keyed by, and
/// what the UI shows to identify a change.
/// </summary>
public sealed class JsonPath
{
    private readonly JsonPath? _parent;
    private readonly string? _property;
    private readonly int _index;

    private JsonPath(JsonPath? parent, string? property, int index)
    {
        _parent = parent;
        _property = property;
        _index = index;
    }

    /// <summary>The document root, <c>$</c>.</summary>
    public static JsonPath Root { get; } = new(null, null, -1);

    /// <summary>This path with a property appended.</summary>
    public JsonPath Property(string name) => new(this, name, -1);

    /// <summary>This path with an array index appended.</summary>
    public JsonPath Index(int index) => new(this, null, index);

    /// <summary>The enclosing path, or null at the root. Lets a tree be rebuilt from a flat list.</summary>
    public JsonPath? Parent => _parent;

    /// <summary>
    /// This step alone, for a tree row that already shows its ancestors: <c>name</c> or <c>[0]</c>.
    /// </summary>
    public string Label => _parent is null
        ? "$"
        : _property ?? $"[{_index}]";

    /// <summary>True when this step is an array index rather than a property.</summary>
    public bool IsIndex => _parent is not null && _property is null;

    public override string ToString()
    {
        if (_parent is null)
        {
            return "$";
        }

        var builder = new StringBuilder();
        Append(builder);
        return builder.ToString();
    }

    /// <summary>
    /// Walks up to the root and writes on the way back down. Recursive, but bounded by nesting depth,
    /// which the parser already caps.
    /// </summary>
    private void Append(StringBuilder builder)
    {
        if (_parent is null)
        {
            builder.Append('$');
            return;
        }

        _parent.Append(builder);

        if (_property is not null)
        {
            // Bracket-quote anything that is not a plain identifier, so a key containing a dot or a
            // space still round-trips to something unambiguous.
            if (IsSimpleIdentifier(_property))
            {
                builder.Append('.').Append(_property);
            }
            else
            {
                builder.Append("['").Append(_property.Replace("'", "\\'")).Append("']");
            }
        }
        else
        {
            builder.Append('[').Append(_index).Append(']');
        }
    }

    private static bool IsSimpleIdentifier(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
