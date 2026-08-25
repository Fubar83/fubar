using System.Collections.Generic;
using System.Linq;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Looks a semantic change up by the line it occupies, so a row in the text view can name the JSON
/// path it belongs to.
///
/// This is the reverse of what the rest of the semantic pass does. <see cref="JsonChangeLines"/> goes
/// change → lines, to decide which rows are significant; this goes line → change, so "ignore the field
/// I am looking at" works from the side-by-side view instead of only from the tree.
///
/// Both sides are indexed separately: a deletion exists only on the left and an insertion only on the
/// right, so a row that has a number on just one side must still resolve.
/// </summary>
public sealed class JsonChangeIndex
{
    /// <summary>An index over no changes - every lookup misses.</summary>
    public static JsonChangeIndex Empty { get; } = new(new Dictionary<int, JsonChange>(), new Dictionary<int, JsonChange>());

    private readonly IReadOnlyDictionary<int, JsonChange> _left;
    private readonly IReadOnlyDictionary<int, JsonChange> _right;

    private JsonChangeIndex(
        IReadOnlyDictionary<int, JsonChange> left,
        IReadOnlyDictionary<int, JsonChange> right)
    {
        _left = left;
        _right = right;
    }

    public static JsonChangeIndex Build(IReadOnlyList<JsonChange>? changes)
    {
        if (changes is null || changes.Count == 0)
        {
            return Empty;
        }

        var left = new Dictionary<int, JsonChange>();
        var right = new Dictionary<int, JsonChange>();

        // Narrowest first, so a line covered by both an object and one of its properties resolves to
        // the property. Ignoring the enclosing object when the user pointed at a single field would
        // hide far more than they asked to hide, and it is not obvious from the row they clicked.
        foreach (var change in changes.OrderBy(Breadth))
        {
            Add(left, change, change.LeftNameSpan, change.Left?.Span);
            Add(right, change, change.RightNameSpan, change.Right?.Span);
        }

        return new JsonChangeIndex(left, right);
    }

    /// <summary>The change covering a row, given its line number on each side. Null when none does.</summary>
    public JsonChange? Find(int? leftLine, int? rightLine)
    {
        if (leftLine is { } left && _left.TryGetValue(left, out var byLeft))
        {
            return byLeft;
        }

        if (rightLine is { } right && _right.TryGetValue(right, out var byRight))
        {
            return byRight;
        }

        return null;
    }

    /// <summary>How many lines a change covers, counting whichever side it has.</summary>
    private static int Breadth(JsonChange change) =>
        (change.Left?.Span.LineCount ?? 0) + (change.Right?.Span.LineCount ?? 0);

    private static void Add(
        Dictionary<int, JsonChange> index,
        JsonChange change,
        SourceSpan nameSpan,
        SourceSpan? valueSpan)
    {
        AddSpan(index, change, nameSpan);

        if (valueSpan is { } span)
        {
            AddSpan(index, change, span);
        }
    }

    private static void AddSpan(Dictionary<int, JsonChange> index, JsonChange change, SourceSpan span)
    {
        if (!span.IsKnown)
        {
            return;
        }

        for (var line = span.StartLine; line <= span.EndLine; line++)
        {
            // TryAdd, not assignment: changes were sorted narrowest-first, so the first one to claim a
            // line is the most specific and must not be overwritten by an enclosing container.
            index.TryAdd(line, change);
        }
    }
}
