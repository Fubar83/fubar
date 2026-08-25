using System.Collections.Generic;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Turns semantic changes into the line numbers they occupy, which is what
/// <see cref="SemanticLineFilter"/> needs to reconcile them with a text alignment.
/// </summary>
public static class JsonChangeLines
{
    /// <summary>
    /// Collects the 1-based line numbers touched by each side of a change set.
    /// </summary>
    public static (IReadOnlySet<int> Left, IReadOnlySet<int> Right) Collect(IReadOnlyList<JsonChange> changes)
    {
        var left = new HashSet<int>();
        var right = new HashSet<int>();

        foreach (var change in changes)
        {
            AddSpan(left, change.LeftNameSpan);
            AddSpan(right, change.RightNameSpan);

            if (change.Left is { } leftNode)
            {
                AddSpan(left, leftNode.Span);
            }

            if (change.Right is { } rightNode)
            {
                AddSpan(right, rightNode.Span);
            }
        }

        return (left, right);
    }

    /// <summary>
    /// Adds every line a span covers.
    ///
    /// Whole-container spans are included deliberately: when an object is replaced wholesale, all of
    /// its lines are part of the change, and marking only the opening brace would leave the body
    /// looking like unchanged context.
    /// </summary>
    private static void AddSpan(HashSet<int> lines, SourceSpan span)
    {
        if (!span.IsKnown)
        {
            return;
        }

        for (var line = span.StartLine; line <= span.EndLine; line++)
        {
            lines.Add(line);
        }
    }
}
