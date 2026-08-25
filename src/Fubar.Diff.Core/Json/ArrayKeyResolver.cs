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
        if (options.MatchArraysByPosition)
        {
            return null;
        }

        // An explicit override wins outright - including over a key the heuristic would have rejected.
        // Someone who names a key has a reason, and second-guessing them here would be unhelpful.
        if (options.ArrayKeyOverrides.TryGetValue(path.ToString(), out var overridden))
        {
            return overridden;
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

            if (obj.Find(candidate)?.Value is not JsonAstScalar scalar)
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
