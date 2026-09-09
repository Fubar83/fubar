using System.Text.Json.Nodes;
using Fubar.Studio.Core.Json;

namespace Fubar.Studio.Core.Snapshots;

/// <summary>
/// Writes one difference from a response into the snapshot it was compared against.
/// </summary>
/// <remarks>
/// <para>What makes a forty-difference wall workable: accept the three that were intended, and what
/// remains is the regression. Accepting everything is the other half, and is the easy half - it is
/// just re-recording.</para>
/// <para>Deliberately field-at-a-time rather than "accept everything under here". A subtree accept
/// reads as one click and can bury a change nobody looked at, which is the failure mode this whole
/// feature exists to refuse.</para>
/// </remarks>
public static class SnapshotAccept
{
    /// <summary>
    /// Copies the value at one CONCRETE path (the kind a difference reports - no wildcards) from the
    /// response into the snapshot, and returns whether anything changed.
    /// </summary>
    /// <remarks>
    /// A path the response does not have is a REMOVAL: the field went away, and accepting that means
    /// taking it out of the snapshot rather than leaving it there to differ forever. A path the
    /// snapshot does not have is the mirror image and is inserted.
    /// </remarks>
    public static bool Field(JsonNode snapshot, JsonNode? response, string path)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var steps = JsonPathMatcher.Steps(path);
        if (steps.Count == 0)
        {
            return false;
        }

        var parent = Walk(snapshot, steps, steps.Count - 1, create: true);
        if (parent is null)
        {
            return false;
        }

        var last = steps[^1];
        var incoming = response is null ? null : Value(response, steps);

        // A DeepClone, because the two documents must not end up sharing a node: a later accept into
        // one would then silently change the other.
        return incoming is null
            ? Remove(parent, last)
            : Set(parent, last, incoming.DeepClone());
    }

    private static JsonNode? Value(JsonNode root, IReadOnlyList<JsonPathMatcher.PathStep> steps)
    {
        var parent = Walk(root, steps, steps.Count - 1, create: false);
        if (parent is null)
        {
            return null;
        }

        var last = steps[^1];

        return last.Name is { } name
            ? (parent as JsonObject)?[name]
            : parent is JsonArray array && last.Index >= 0 && last.Index < array.Count
                ? array[last.Index]
                : null;
    }

    /// <summary>Walks the first <paramref name="depth"/> steps, optionally creating the objects a
    /// missing path needs - which is what lets a field the snapshot never had be accepted into it.</summary>
    private static JsonNode? Walk(
        JsonNode root, IReadOnlyList<JsonPathMatcher.PathStep> steps, int depth, bool create)
    {
        var node = root;

        for (var i = 0; i < depth; i++)
        {
            var step = steps[i];

            if (step.Name is { } name)
            {
                if (node is not JsonObject obj)
                {
                    return null;
                }

                if (obj[name] is null)
                {
                    if (!create)
                    {
                        return null;
                    }

                    obj[name] = new JsonObject();
                }

                node = obj[name]!;
                continue;
            }

            if (node is not JsonArray array || step.Index < 0 || step.Index >= array.Count)
            {
                return null;
            }

            node = array[step.Index]!;
        }

        return node;
    }

    private static bool Set(JsonNode parent, JsonPathMatcher.PathStep step, JsonNode value)
    {
        if (step.Name is { } name && parent is JsonObject obj)
        {
            obj[name] = value;
            return true;
        }

        if (parent is JsonArray array && step.Index >= 0)
        {
            // Beyond the end is an append rather than a failure: the response grew, and that is
            // exactly the difference being accepted.
            if (step.Index < array.Count)
            {
                array[step.Index] = value;
            }
            else
            {
                array.Add(value);
            }

            return true;
        }

        return false;
    }

    private static bool Remove(JsonNode parent, JsonPathMatcher.PathStep step)
    {
        if (step.Name is { } name && parent is JsonObject obj)
        {
            return obj.Remove(name);
        }

        if (parent is JsonArray array && step.Index >= 0 && step.Index < array.Count)
        {
            array.RemoveAt(step.Index);
            return true;
        }

        return false;
    }
}
