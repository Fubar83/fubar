using System;
using System.Collections.Generic;
using System.Linq;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// What an array at one path could be matched by, so the UI can offer it rather than making the user
/// type a field name they have to go and look up.
/// </summary>
/// <param name="Path">The array's JSON path, e.g. <c>$.users</c>.</param>
/// <param name="Suggested">
/// The key auto-detection would pick, or null when it would fall back to position. Offered first
/// because it is almost always right - it is the same answer the comparison is already using.
/// </param>
/// <param name="Candidates">
/// Every field that could serve as a key: present on every element of both sides, holding a scalar.
/// Fields that are missing somewhere, or hold an object, cannot identify an element and are left out
/// rather than offered and then quietly ignored.
/// </param>
/// <param name="ElementsAreObjects">
/// False for an array of scalars or of mixed shapes, where there is no field to choose - the only
/// meaningful choice left is whether order matters.
/// </param>
public sealed record ArrayKeyChoices(
    string Path,
    string? Suggested,
    IReadOnlyList<string> Candidates,
    bool ElementsAreObjects);

/// <summary>
/// Finds the arrays in a comparison and works out what each could be keyed by.
///
/// Exists so the change tree can offer a real choice on a right-click - "match by id", "match by
/// sku" - instead of a text box the user has to fill from memory. Both documents are walked together
/// and only fields present on EVERY element of BOTH sides are offered: a key that is missing on one
/// element silently fails to match it, which produces a diff that looks like data loss.
/// </summary>
public static class ArrayKeyScanner
{
    /// <summary>How deep to look for candidate keys inside an element. One level of nesting.</summary>
    private const int MaxKeyDepth = 2;

    /// <summary>Every array reachable in both documents, keyed by path.</summary>
    public static IReadOnlyDictionary<string, ArrayKeyChoices> Scan(
        JsonAstNode? left,
        JsonAstNode? right,
        JsonComparisonOptions options)
    {
        var found = new Dictionary<string, ArrayKeyChoices>(StringComparer.Ordinal);

        if (left is not null && right is not null)
        {
            Walk(left, right, JsonPath.Root, options, found);
        }

        return found;
    }

    private static void Walk(
        JsonAstNode left,
        JsonAstNode right,
        JsonPath path,
        JsonComparisonOptions options,
        Dictionary<string, ArrayKeyChoices> found)
    {
        switch (left)
        {
            case JsonAstObject leftObject when right is JsonAstObject rightObject:
                foreach (var property in leftObject.Properties)
                {
                    if (rightObject.Find(property.Name) is { } match)
                    {
                        Walk(property.Value, match.Value, path.Property(property.Name), options, found);
                    }
                }

                break;

            case JsonAstArray leftArray when right is JsonAstArray rightArray:
                found[path.ToString()] = Describe(leftArray, rightArray, path, options);

                // Into the elements as well: an array of objects can hold arrays of its own, and the
                // first element is enough to reach them - the paths use [*] semantics anyway, and a
                // nested array's own choices do not vary by index.
                if (leftArray.Items.Count > 0 && rightArray.Items.Count > 0)
                {
                    Walk(leftArray.Items[0], rightArray.Items[0], path.Index(0), options, found);
                }

                break;
        }
    }

    private static ArrayKeyChoices Describe(
        JsonAstArray left,
        JsonAstArray right,
        JsonPath path,
        JsonComparisonOptions options)
    {
        var elementsAreObjects =
            left.Items.Count + right.Items.Count > 0
            && left.Items.All(i => i is JsonAstObject)
            && right.Items.All(i => i is JsonAstObject);

        return new ArrayKeyChoices(
            path.ToString(),
            ArrayKeyResolver.Resolve(left, right, path, options),
            elementsAreObjects ? Candidates(left, right) : [],
            elementsAreObjects);
    }

    /// <summary>
    /// The fields that could identify these elements: scalar, present everywhere on both sides, and
    /// distinct within each side. That is the same bar <see cref="ArrayKeyResolver"/> sets, so
    /// choosing one from this list always produces a key that actually matches - offering a field that
    /// then silently fails would be worse than offering nothing.
    /// </summary>
    private static IReadOnlyList<string> Candidates(JsonAstArray left, JsonAstArray right)
    {
        var names = NamesIn(left.Items.Concat(right.Items));

        return [.. names.Where(name => Usable(name, left) && Usable(name, right))];
    }

    /// <summary>Every scalar-valued path within the elements, to a shallow depth.</summary>
    private static IReadOnlyList<string> NamesIn(IEnumerable<JsonAstNode> items)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item is JsonAstObject obj)
            {
                Collect(obj, prefix: null, depth: 1, names, seen);
            }
        }

        return names;
    }

    private static void Collect(
        JsonAstObject obj,
        string? prefix,
        int depth,
        List<string> names,
        HashSet<string> seen)
    {
        foreach (var property in obj.Properties)
        {
            var name = prefix is null ? property.Name : prefix + "." + property.Name;

            switch (property.Value)
            {
                case JsonAstScalar when seen.Add(name):
                    names.Add(name);
                    break;

                case JsonAstObject nested when depth < MaxKeyDepth:
                    // One level in, and no further: identity lives at the top or just under it, and a
                    // deep walk would offer a menu nobody can read.
                    Collect(nested, name, depth + 1, names, seen);
                    break;
            }
        }
    }

    private static bool Usable(string name, JsonAstArray array)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in array.Items)
        {
            if (item is not JsonAstObject obj || ArrayKeyResolver.ValueFor(obj, name) is not { } scalar)
            {
                return false;
            }

            if (scalar.Kind == JsonAstKind.Null || !seen.Add(ArrayKeyResolver.KeyOf(scalar)))
            {
                return false;
            }
        }

        return true;
    }
}
