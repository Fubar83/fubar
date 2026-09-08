using System.Text.Json.Nodes;

namespace Fubar.Studio.Core.Snapshots;

/// <summary>
/// Replaces every value a path pattern matches. What redaction and normalisation are both built on.
/// </summary>
/// <remarks>
/// <para>
/// A small matcher rather than <c>Fubar.Diff.Core.Json.JsonPathPattern</c>: <c>Fubar.Studio.Core</c>
/// depends on nothing but the BCL, and this runs on the WRITE side where no comparison is happening.
/// The syntax is deliberately the same subset, so a path that works in an ignore rule works here.
/// </para>
/// <para>Supported: <c>$</c>, <c>.name</c>, <c>..name</c> (at any depth), <c>[*]</c>, <c>[0]</c>.
/// Anything else matches nothing rather than throwing - a rule that cannot be parsed must not stop a
/// snapshot being taken, and <see cref="IsSupported"/> is how a caller can warn instead.</para>
/// </remarks>
public static class JsonPathRewriter
{
    /// <summary>Replaces every match, returning how many there were.</summary>
    public static int Apply(JsonNode? root, string pattern, string replacement)
    {
        if (root is null || !TryParse(pattern, out var steps))
        {
            return 0;
        }

        var count = 0;
        Walk(root, steps, 0, (parent, key, index) =>
        {
            Set(parent, key, index, JsonValue.Create(replacement));
            count++;
        });

        return count;
    }

    /// <summary>Whether the pattern is one this understands, so a caller can report the ones it is
    /// about to ignore rather than silently doing nothing.</summary>
    public static bool IsSupported(string pattern) => TryParse(pattern, out _);

    private sealed record Step(string? Name, bool Recursive, bool AllIndices, int? Index);

    private static bool TryParse(string pattern, out List<Step> steps)
    {
        steps = [];
        if (string.IsNullOrWhiteSpace(pattern) || pattern[0] != '$')
        {
            return false;
        }

        var i = 1;
        while (i < pattern.Length)
        {
            if (pattern[i] == '.')
            {
                var recursive = i + 1 < pattern.Length && pattern[i + 1] == '.';
                i += recursive ? 2 : 1;

                var start = i;
                while (i < pattern.Length && pattern[i] is not ('.' or '['))
                {
                    i++;
                }

                if (i == start)
                {
                    return false;
                }

                steps.Add(new Step(pattern[start..i], recursive, false, null));
                continue;
            }

            if (pattern[i] == '[')
            {
                var close = pattern.IndexOf(']', i);
                if (close < 0)
                {
                    return false;
                }

                var inner = pattern[(i + 1)..close];
                i = close + 1;

                if (inner == "*")
                {
                    steps.Add(new Step(null, false, true, null));
                }
                else if (int.TryParse(inner, out var index))
                {
                    steps.Add(new Step(null, false, false, index));
                }
                else
                {
                    return false;
                }

                continue;
            }

            return false;
        }

        return steps.Count > 0;
    }

    private static void Walk(JsonNode node, List<Step> steps, int depth, Action<JsonNode, string?, int?> hit)
    {
        if (depth >= steps.Count)
        {
            return;
        }

        var step = steps[depth];
        var last = depth == steps.Count - 1;

        if (step.Name is { } name)
        {
            foreach (var (parent, key) in FindProperties(node, name, step.Recursive))
            {
                if (last)
                {
                    hit(parent, key, null);
                }
                else if (parent is JsonObject obj && obj[key] is { } child)
                {
                    Walk(child, steps, depth + 1, hit);
                }
            }

            return;
        }

        if (node is not JsonArray array)
        {
            return;
        }

        for (var index = 0; index < array.Count; index++)
        {
            if (!step.AllIndices && step.Index != index)
            {
                continue;
            }

            if (last)
            {
                hit(array, null, index);
            }
            else if (array[index] is { } element)
            {
                Walk(element, steps, depth + 1, hit);
            }
        }
    }

    /// <summary>Every object carrying <paramref name="name"/> - just this one, or any descendant when
    /// the step was a recursive <c>..</c>.</summary>
    private static IEnumerable<(JsonNode Parent, string Key)> FindProperties(JsonNode node, string name, bool recursive)
    {
        if (node is JsonObject obj && obj.ContainsKey(name))
        {
            yield return (obj, name);
        }

        if (!recursive)
        {
            yield break;
        }

        foreach (var child in Children(node))
        {
            foreach (var found in FindProperties(child, name, true))
            {
                yield return found;
            }
        }
    }

    private static IEnumerable<JsonNode> Children(JsonNode node) => node switch
    {
        JsonObject obj => obj.Select(p => p.Value).OfType<JsonNode>(),
        JsonArray array => array.OfType<JsonNode>(),
        _ => [],
    };

    private static void Set(JsonNode parent, string? key, int? index, JsonNode? value)
    {
        if (key is not null && parent is JsonObject obj)
        {
            obj[key] = value;
        }
        else if (index is { } i && parent is JsonArray array)
        {
            array[i] = value;
        }
    }
}
