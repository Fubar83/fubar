using System.Collections.Generic;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Decides which property, if any, identifies the elements of an array.
///
/// This is what stops one inserted element from marking everything after it as changed. Compared
/// positionally, inserting at the front of a 500-element array reports 500 differences; matched by
/// <c>id</c>, it reports one.
///
/// Separate from the differ so the heuristic can be tested on its own and swapped without touching the
/// comparison logic - it is a guess, and guesses need to be easy to revise.
/// </summary>
public static class ArrayKeyResolver
{
    /// <summary>
    /// Candidate key names, most specific first. <c>id</c> before <c>name</c> deliberately: a name is
    /// often a label that can legitimately change, whereas an id is meant to be stable, so preferring
    /// it produces better matches when both are present.
    /// </summary>
    public static IReadOnlyList<string> CandidateNames { get; } =
        ["id", "_id", "uuid", "guid", "key", "name"];

    /// <summary>
    /// Finds the identity key for an array, or null to fall back to positional matching.
    ///
    /// A candidate qualifies only if EVERY element is an object that has it, holding a scalar, and no
    /// two elements share a value. A key that is missing or duplicated somewhere would silently
    /// mismatch elements, which is worse than not matching by key at all.
    /// </summary>
    /// <param name="left">The array on the left, whose elements must also satisfy the key.</param>
    /// <param name="right">The array on the right.</param>
    /// <param name="path">The array's path, used to look up an override.</param>
    /// <param name="options">Overrides and the positional-matching switch.</param>
    public static string? Resolve(
        JsonAstArray left,
        JsonAstArray right,
        JsonPath path,
        JsonComparisonOptions options)
    {
        var key = path.ToString();

        // An explicit override wins outright - including over a key the heuristic would have rejected,
        // and including when everything else is set to positional. Someone who names a key for THIS
        // array has said what they want about it, and second-guessing them here would be unhelpful.
        if (options.ArrayKeyOverrides.TryGetValue(key, out var overridden))
        {
            return overridden;
        }

        if (options.MatchArraysByPosition || Contains(options.PositionalArrays, key))
        {
            return null;
        }

        // Matching by key is meaningless when there is nothing on one side to match against.
        if (left.Items.Count == 0 || right.Items.Count == 0)
        {
            return null;
        }

        foreach (var candidate in CandidateNames)
        {
            if (Qualifies(candidate, left) && Qualifies(candidate, right))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool Contains(IReadOnlyList<string> paths, string path)
    {
        foreach (var candidate in paths)
        {
            if (string.Equals(candidate, path, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The value a key names within one element, following <c>.</c> through nested objects.
    ///
    /// A dotted path rather than a bare name because identity is not always at the top level -
    /// <c>meta.id</c> and <c>attributes.sku</c> are ordinary shapes, and an array keyed on one of them
    /// is exactly the case the auto-detection cannot help with. A single name is just a path of one
    /// segment, so nothing about the common case changes.
    /// </summary>
    public static JsonAstScalar? ValueFor(JsonAstObject element, string keyPath)
    {
        JsonAstNode? current = element;

        foreach (var segment in keyPath.Split('.'))
        {
            if (current is not JsonAstObject obj || obj.Find(segment) is not { } property)
            {
                return null;
            }

            current = property.Value;
        }

        return current as JsonAstScalar;
    }

    /// <summary>
    /// Whether every element of one array carries this key with a distinct scalar value.
    /// </summary>
    private static bool Qualifies(string candidate, JsonAstArray array)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var item in array.Items)
        {
            if (item is not JsonAstObject obj)
            {
                return false;
            }

            if (ValueFor(obj, candidate) is not { } scalar)
            {
                return false;
            }

            // Null is a placeholder rather than an identity - several elements can legitimately carry
            // it, so it cannot distinguish them.
            if (scalar.Kind == JsonAstKind.Null)
            {
                return false;
            }

            if (!seen.Add(KeyOf(scalar)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The value used to match elements. Includes the kind so that the string <c>"1"</c> and the
    /// number <c>1</c> are not treated as the same element.
    /// </summary>
    public static string KeyOf(JsonAstScalar scalar) => $"{scalar.Kind}:{scalar.ComparisonText}";
}
