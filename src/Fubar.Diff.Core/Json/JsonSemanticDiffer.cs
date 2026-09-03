using System.Collections.Generic;
using System.Linq;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Compares two JSON documents by structure rather than by text.
///
/// The difference this makes: reformatting a file, or a serializer emitting properties in a different
/// order, produces no changes at all; and an element inserted into a keyed array marks that one
/// element rather than every element after it. Both are the common cases that make a text diff of JSON
/// hard to read.
///
/// Pure - no I/O, no options beyond what is passed in - so every rule below is testable directly.
/// </summary>
public static class JsonSemanticDiffer
{
    /// <summary>Compares two parsed documents and returns every difference, in document order.</summary>
    public static IReadOnlyList<JsonChange> Compare(
        JsonAstNode left,
        JsonAstNode right,
        JsonComparisonOptions options)
    {
        var changes = new List<JsonChange>();
        CompareNode(left, right, JsonPath.Root, options, changes);

        // Filtered here rather than by a caller, so every consumer of a semantic comparison - the
        // tree, the line filter behind the text view, the diff map, navigation - agrees on what
        // counts as a difference. A view that filtered for itself would disagree with the others.
        return JsonIgnoreRules.From(options.IgnoredPaths).Mark(changes);
    }

    private static void CompareNode(
        JsonAstNode left,
        JsonAstNode right,
        JsonPath path,
        JsonComparisonOptions options,
        List<JsonChange> changes)
    {
        // A change of kind is a whole-value replacement - descending into an object to compare it with
        // an array would produce a pile of meaningless per-property differences.
        if (left.Kind != right.Kind)
        {
            changes.Add(new JsonChange(path, ChangeKind.Modified, left, right));
            return;
        }

        switch (left)
        {
            case JsonAstObject leftObject when right is JsonAstObject rightObject:
                CompareObjects(leftObject, rightObject, path, options, changes);
                break;

            case JsonAstArray leftArray when right is JsonAstArray rightArray:
                CompareArrays(leftArray, rightArray, path, options, changes);
                break;

            case JsonAstScalar leftScalar when right is JsonAstScalar rightScalar:
                if (!string.Equals(leftScalar.ComparisonText, rightScalar.ComparisonText, System.StringComparison.Ordinal))
                {
                    changes.Add(new JsonChange(path, ChangeKind.Modified, left, right));
                }

                break;
        }
    }

    private static void CompareObjects(
        JsonAstObject left,
        JsonAstObject right,
        JsonPath path,
        JsonComparisonOptions options,
        List<JsonChange> changes)
    {
        // Properties are matched by NAME, never by position - that is what makes reordering invisible.
        var rightByName = new Dictionary<string, JsonAstProperty>(System.StringComparer.Ordinal);
        foreach (var property in right.Properties)
        {
            rightByName.TryAdd(property.Name, property);
        }

        var leftNames = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var leftProperty in left.Properties)
        {
            leftNames.Add(leftProperty.Name);
            var childPath = path.Property(leftProperty.Name);

            if (!rightByName.TryGetValue(leftProperty.Name, out var rightProperty))
            {
                if (!IsIgnorableNull(leftProperty.Value, options))
                {
                    changes.Add(new JsonChange(childPath, ChangeKind.Deleted, leftProperty.Value, null)
                    {
                        LeftNameSpan = leftProperty.NameSpan,
                    });
                }

                continue;
            }

            CompareNode(leftProperty.Value, rightProperty.Value, childPath, options, changes);
        }

        foreach (var rightProperty in right.Properties)
        {
            if (leftNames.Contains(rightProperty.Name))
            {
                continue;
            }

            if (IsIgnorableNull(rightProperty.Value, options))
            {
                continue;
            }

            changes.Add(new JsonChange(path.Property(rightProperty.Name), ChangeKind.Inserted, null, rightProperty.Value)
            {
                RightNameSpan = rightProperty.NameSpan,
            });
        }

        if (options.ReportPropertyOrder)
        {
            ReportReorderedProperties(left, right, path, changes);
        }
    }

    /// <summary>
    /// An explicit null counts as "not there" when the caller asked for that, so a serializer emitting
    /// <c>"x": null</c> where another omits <c>x</c> is not reported.
    /// </summary>
    private static bool IsIgnorableNull(JsonAstNode node, JsonComparisonOptions options) =>
        options.IgnoreNullVsMissing && node.Kind == JsonAstKind.Null;

    /// <summary>
    /// Flags properties whose relative order differs.
    ///
    /// Compares the sequence of shared names on each side and reports the ones that are not in the
    /// longest common subsequence - i.e. the minimum set that actually had to move, rather than every
    /// property after the first displaced one.
    /// </summary>
    private static void ReportReorderedProperties(
        JsonAstObject left,
        JsonAstObject right,
        JsonPath path,
        List<JsonChange> changes)
    {
        var rightNames = right.Properties.Select(p => p.Name).ToList();
        var rightSet = new HashSet<string>(rightNames, System.StringComparer.Ordinal);

        var sharedLeft = left.Properties.Where(p => rightSet.Contains(p.Name)).ToList();
        var leftSet = new HashSet<string>(sharedLeft.Select(p => p.Name), System.StringComparer.Ordinal);
        var sharedRight = right.Properties.Where(p => leftSet.Contains(p.Name)).ToList();

        var stable = LongestCommonSubsequence(
            sharedLeft.Select(p => p.Name).ToList(),
            sharedRight.Select(p => p.Name).ToList());

        foreach (var leftProperty in sharedLeft)
        {
            if (stable.Contains(leftProperty.Name))
            {
                continue;
            }

            var rightProperty = right.Find(leftProperty.Name)!;

            changes.Add(new JsonChange(path.Property(leftProperty.Name), ChangeKind.Modified, leftProperty.Value, rightProperty.Value)
            {
                LeftNameSpan = leftProperty.NameSpan,
                RightNameSpan = rightProperty.NameSpan,
                IsReorder = true,
            });
        }
    }

    private static void CompareArrays(
        JsonAstArray left,
        JsonAstArray right,
        JsonPath path,
        JsonComparisonOptions options,
        List<JsonChange> changes)
    {
        var key = ArrayKeyResolver.Resolve(left, right, path, options);

        switch (ModeFor(path.ToString(), key, options))
        {
            case ArrayMatchMode.Unordered:
                CompareArraysUnordered(left, right, path, options, changes);
                return;

            case ArrayMatchMode.Key when key is not null:
                CompareArraysByKey(left, right, path, key, options, changes);
                return;

            default:
                CompareArraysByPosition(left, right, path, options, changes);
                return;
        }
    }

    /// <summary>
    /// Which mode an array is compared with. Public because the menu asks the same question, and two
    /// implementations of this precedence would eventually give the check mark and the comparison
    /// different answers.
    ///
    /// <para>Most specific instruction first. An explicit key names a field, so it wins outright. An
    /// explicit "this one is positional" beats an explicit "this one is unordered": that pair is a
    /// contradiction only the user can have written, and positional is its conservative half, since
    /// reporting a reorder nobody minds is a smaller failure than hiding one that matters. The GLOBAL
    /// unordered switch ranks below automatic key detection, because where a key exists it already
    /// ignores order and says which field of which element changed as well.</para>
    /// </summary>
    public static ArrayMatchMode ModeFor(string path, string? resolvedKey, JsonComparisonOptions options)
    {
        if (options.ArrayKeyOverrides.ContainsKey(path))
        {
            return ArrayMatchMode.Key;
        }

        if (options.MatchArraysByPosition || PathListed(options.PositionalArrays, path))
        {
            return ArrayMatchMode.Position;
        }

        if (PathListed(options.UnorderedArrays, path))
        {
            return ArrayMatchMode.Unordered;
        }

        if (resolvedKey is not null)
        {
            return ArrayMatchMode.Key;
        }

        return options.IgnoreArrayOrder ? ArrayMatchMode.Unordered : ArrayMatchMode.Position;
    }

    private static bool PathListed(IReadOnlyList<string> paths, string path)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            if (string.Equals(paths[i], path, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Compares an array as an unordered collection: elements are matched by their whole VALUE, so
    /// nothing needs a field to be identified by and a list of strings works as well as a list of
    /// objects.
    ///
    /// <para>A MULTISET, not a set. <c>["A","A","B"]</c> against <c>["A","B"]</c> has genuinely lost an
    /// element, and set semantics would call the two equal - the one answer a comparison must never
    /// give. Each match is consumed.</para>
    ///
    /// <para>What is left over after the exact matches is compared PAIRWISE, in order, rather than
    /// reported as a pile of deletions and insertions. Two reasons, and both are what makes this usable:
    /// an element that changed in one field still gets a field-level diff saying which one, and ignore
    /// rules still apply to it - matching purely by value would report a whole element as replaced
    /// because a timestamp inside it moved, and the rule covering that timestamp would never get to
    /// speak.</para>
    /// </summary>
    private static void CompareArraysUnordered(
        JsonAstArray left,
        JsonAstArray right,
        JsonPath path,
        JsonComparisonOptions options,
        List<JsonChange> changes)
    {
        // Right-hand elements by signature, each usable once.
        var available = new Dictionary<string, Queue<int>>(System.StringComparer.Ordinal);
        for (var i = 0; i < right.Items.Count; i++)
        {
            var signature = JsonValueSignature.Of(right.Items[i]);
            if (!available.TryGetValue(signature, out var queue))
            {
                available[signature] = queue = new Queue<int>();
            }

            queue.Enqueue(i);
        }

        var matchedRight = new HashSet<int>();
        var unmatchedLeft = new List<int>();

        for (var i = 0; i < left.Items.Count; i++)
        {
            var signature = JsonValueSignature.Of(left.Items[i]);

            if (available.TryGetValue(signature, out var queue) && queue.Count > 0)
            {
                // Identical by value: nothing to report, whatever position either of them sits at.
                matchedRight.Add(queue.Dequeue());
                continue;
            }

            unmatchedLeft.Add(i);
        }

        var unmatchedRight = new List<int>();
        for (var i = 0; i < right.Items.Count; i++)
        {
            if (!matchedRight.Contains(i))
            {
                unmatchedRight.Add(i);
            }
        }

        var shared = System.Math.Min(unmatchedLeft.Count, unmatchedRight.Count);
        for (var i = 0; i < shared; i++)
        {
            CompareNode(
                left.Items[unmatchedLeft[i]],
                right.Items[unmatchedRight[i]],
                path.Index(unmatchedLeft[i]),
                options,
                changes);
        }

        for (var i = shared; i < unmatchedLeft.Count; i++)
        {
            changes.Add(new JsonChange(path.Index(unmatchedLeft[i]), ChangeKind.Deleted, left.Items[unmatchedLeft[i]], null));
        }

        for (var i = shared; i < unmatchedRight.Count; i++)
        {
            changes.Add(new JsonChange(path.Index(unmatchedRight[i]), ChangeKind.Inserted, null, right.Items[unmatchedRight[i]]));
        }
    }

    private static void CompareArraysByPosition(
        JsonAstArray left,
        JsonAstArray right,
        JsonPath path,
        JsonComparisonOptions options,
        List<JsonChange> changes)
    {
        var shared = System.Math.Min(left.Items.Count, right.Items.Count);

        for (var i = 0; i < shared; i++)
        {
            CompareNode(left.Items[i], right.Items[i], path.Index(i), options, changes);
        }

        for (var i = shared; i < left.Items.Count; i++)
        {
            changes.Add(new JsonChange(path.Index(i), ChangeKind.Deleted, left.Items[i], null));
        }

        for (var i = shared; i < right.Items.Count; i++)
        {
            changes.Add(new JsonChange(path.Index(i), ChangeKind.Inserted, null, right.Items[i]));
        }
    }

    /// <summary>
    /// Matches elements by identity key, so an insertion or a reordering affects only the elements
    /// that actually changed.
    /// </summary>
    private static void CompareArraysByKey(
        JsonAstArray left,
        JsonAstArray right,
        JsonPath path,
        string key,
        JsonComparisonOptions options,
        List<JsonChange> changes)
    {
        var rightByKey = new Dictionary<string, (JsonAstNode Node, int Index)>(System.StringComparer.Ordinal);
        for (var i = 0; i < right.Items.Count; i++)
        {
            if (KeyOf(right.Items[i], key) is { } k)
            {
                rightByKey[k] = (right.Items[i], i);
            }
        }

        var matchedRight = new HashSet<int>();

        for (var i = 0; i < left.Items.Count; i++)
        {
            var leftKey = KeyOf(left.Items[i], key);

            if (leftKey is null || !rightByKey.TryGetValue(leftKey, out var match))
            {
                changes.Add(new JsonChange(path.Index(i), ChangeKind.Deleted, left.Items[i], null));
                continue;
            }

            matchedRight.Add(match.Index);

            // Report against the LEFT index: the change is shown beside the element the user is
            // looking at on the left, and the right index is recoverable from the node's span.
            CompareNode(left.Items[i], match.Node, path.Index(i), options, changes);
        }

        for (var i = 0; i < right.Items.Count; i++)
        {
            if (!matchedRight.Contains(i))
            {
                changes.Add(new JsonChange(path.Index(i), ChangeKind.Inserted, null, right.Items[i]));
            }
        }
    }

    // Through ArrayKeyResolver.ValueFor rather than Find, so a key naming something nested -
    // "meta.id" - matches elements the same way the resolver decided it could.
    private static string? KeyOf(JsonAstNode node, string key) =>
        node is JsonAstObject obj && ArrayKeyResolver.ValueFor(obj, key) is { } scalar
            ? ArrayKeyResolver.KeyOf(scalar)
            : null;

    /// <summary>
    /// The names that keep their relative order between the two sequences. Standard LCS - the
    /// sequences here are one object's property names, so the quadratic table is not a concern.
    /// </summary>
    private static HashSet<string> LongestCommonSubsequence(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var lengths = new int[left.Count + 1, right.Count + 1];

        for (var i = 1; i <= left.Count; i++)
        {
            for (var j = 1; j <= right.Count; j++)
            {
                lengths[i, j] = string.Equals(left[i - 1], right[j - 1], System.StringComparison.Ordinal)
                    ? lengths[i - 1, j - 1] + 1
                    : System.Math.Max(lengths[i - 1, j], lengths[i, j - 1]);
            }
        }

        var result = new HashSet<string>(System.StringComparer.Ordinal);
        var x = left.Count;
        var y = right.Count;

        while (x > 0 && y > 0)
        {
            if (string.Equals(left[x - 1], right[y - 1], System.StringComparison.Ordinal))
            {
                result.Add(left[x - 1]);
                x--;
                y--;
            }
            else if (lengths[x - 1, y] >= lengths[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        return result;
    }
}
