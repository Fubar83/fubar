using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// A stable string standing for a node's whole VALUE, so two nodes can be recognised as the same thing
/// without comparing them field by field.
///
/// <para>Used to match the elements of an array whose order does not matter. An identity key answers
/// "which element is this?" for objects that carry an id; a signature answers it for everything else -
/// a list of strings, a list of numbers, a list of objects with no id at all.</para>
///
/// <para><b>Property order does not affect the signature</b>, matching the rest of the comparison: JSON
/// objects are unordered by definition, and two elements differing only in key order are the same
/// element. <b>Nested array order DOES.</b> Opting one array out of ordering says nothing about the
/// arrays inside it, and quietly making those unordered too would hide differences the user never asked
/// to hide - a nested array that should also be unordered gets its own rule.</para>
/// </summary>
public static class JsonValueSignature
{
    // Control characters, so they cannot occur in the data and collide: ["a,b"] and ["a","b"] must not
    // sign the same. Written as escapes rather than literals so the file's encoding cannot eat them.
    private const char FieldSeparator = '\u0001';
    private const char OpenObject = '\u0002';
    private const char CloseObject = '\u0003';
    private const char OpenArray = '\u0004';
    private const char CloseArray = '\u0005';
    private const char NameValue = '\u0006';

    /// <summary>Builds the signature.</summary>
    public static string Of(JsonAstNode node)
    {
        var builder = new StringBuilder();
        Write(node, builder);
        return builder.ToString();
    }

    private static void Write(JsonAstNode node, StringBuilder builder)
    {
        switch (node)
        {
            case JsonAstScalar scalar:
                // The same form the identity-key matcher uses, so a keyed match and a signature match
                // agree about when two scalars are the same value.
                builder.Append(ArrayKeyResolver.KeyOf(scalar));
                break;

            case JsonAstObject obj:
                builder.Append(OpenObject);
                foreach (var property in Sorted(obj.Properties))
                {
                    builder.Append(property.Name).Append(NameValue);
                    Write(property.Value, builder);
                    builder.Append(FieldSeparator);
                }

                builder.Append(CloseObject);
                break;

            case JsonAstArray array:
                builder.Append(OpenArray);
                foreach (var item in array.Items)
                {
                    Write(item, builder);
                    builder.Append(FieldSeparator);
                }

                builder.Append(CloseArray);
                break;
        }
    }

    /// <summary>
    /// Properties in name order, so key order cannot change the signature.
    ///
    /// <c>OrderBy</c> rather than <c>List.Sort</c> deliberately: it is stable, so duplicate names keep
    /// their relative order and therefore the first-wins meaning duplicates already have elsewhere in
    /// this codebase, instead of an unstable sort inventing a second rule for them.
    /// </summary>
    private static IEnumerable<JsonAstProperty> Sorted(IReadOnlyList<JsonAstProperty> properties) =>
        properties.Count <= 1
            ? properties
            : properties.OrderBy(p => p.Name, System.StringComparer.Ordinal);
}
