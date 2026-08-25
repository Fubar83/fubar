using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// A path expression matched against a <see cref="JsonPath"/>, used to say "never report changes
/// here". Exact paths cover the simple case; the two wildcards cover the cases that actually drive
/// people to this feature.
///
/// <code>
/// $.meta.requestId      exactly that property
/// $.items[*].updatedAt  that property in EVERY element - an array's worth of noise, one rule
/// $..timestamp          that property at any depth
/// </code>
///
/// Matching a path also matches everything BENEATH it: ignoring <c>$.meta</c> ignores the whole
/// object. Ignoring a container and then still being shown its contents would make the rule look
/// broken, and there is no sensible reading of "ignore this object, but not its fields".
/// </summary>
public sealed class JsonPathPattern
{
    private enum Kind
    {
        Property,
        Index,
        AnyIndex,

        /// <summary>The <c>..</c> of <c>$..name</c> - matches any run of segments, including none.</summary>
        AnyDescendant,
    }

    private readonly record struct Segment(Kind Kind, string? Name, int Index);

    private readonly List<Segment> _segments;

    /// <summary>The pattern as written, which is what gets persisted and shown back to the user.</summary>
    public string Text { get; }

    private JsonPathPattern(string text, List<Segment> segments)
    {
        Text = text;
        _segments = segments;
    }

    /// <summary>
    /// Parses a pattern. Returns false rather than throwing on anything malformed: these come from a
    /// hand-editable request file, and one bad rule must not stop a response from being compared.
    /// </summary>
    public static bool TryParse(string? text, out JsonPathPattern? pattern)
    {
        pattern = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var segments = new List<Segment>();
        var i = 0;

        // A leading "$" is optional, so both "$.a.b" and "a.b" work - the second is what people type.
        if (trimmed.StartsWith('$'))
        {
            i = 1;
        }

        while (i < trimmed.Length)
        {
            if (trimmed[i] == '.')
            {
                if (i + 1 < trimmed.Length && trimmed[i + 1] == '.')
                {
                    segments.Add(new Segment(Kind.AnyDescendant, null, -1));
                    i += 2;
                    continue;
                }

                i++;
                continue;
            }

            if (trimmed[i] == '[')
            {
                var close = trimmed.IndexOf(']', i);
                if (close < 0)
                {
                    return false;
                }

                var inner = trimmed[(i + 1)..close];
                if (inner == "*")
                {
                    segments.Add(new Segment(Kind.AnyIndex, null, -1));
                }
                else if (int.TryParse(inner, out var index) && index >= 0)
                {
                    segments.Add(new Segment(Kind.Index, null, index));
                }
                else
                {
                    return false;
                }

                i = close + 1;
                continue;
            }

            var start = i;
            while (i < trimmed.Length && trimmed[i] != '.' && trimmed[i] != '[')
            {
                i++;
            }

            var name = trimmed[start..i];
            if (name.Length == 0)
            {
                return false;
            }

            segments.Add(new Segment(Kind.Property, name, -1));
        }

        // "$" alone would ignore the entire document, which is never what someone means and silently
        // turns every comparison into "no differences".
        if (segments.Count == 0)
        {
            return false;
        }

        pattern = new JsonPathPattern(Normalize(segments), segments);
        return true;
    }

    /// <summary>
    /// The rule to create from a change at <paramref name="path"/>, with every array index replaced by
    /// <c>[*]</c>.
    ///
    /// Deliberately broader than the path clicked. A noisy field is noisy in every element - ignoring
    /// <c>$.items[0].syncedAt</c> and still being shown <c>$.items[1].syncedAt</c> would look broken,
    /// and the fix would be for the user to know the wildcard syntax and hand-edit the rule. The
    /// element index is almost never the point; the field is.
    /// </summary>
    public static string Generalize(string path)
    {
        var builder = new StringBuilder(path.Length);

        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] != '[')
            {
                builder.Append(path[i]);
                continue;
            }

            var close = path.IndexOf(']', i);
            if (close < 0)
            {
                builder.Append(path[i..]);
                break;
            }

            var inner = path[(i + 1)..close];
            builder.Append(inner.Length > 0 && inner.All(char.IsDigit) ? "[*]" : path[i..(close + 1)]);
            i = close;
        }

        return builder.ToString();
    }

    /// <summary>True when this pattern covers <paramref name="path"/> or any ancestor of it.</summary>
    public bool Matches(JsonPath path)
    {
        var segments = SegmentsOf(path);

        // Every prefix, so a rule on a container also covers what is inside it.
        for (var length = 0; length <= segments.Count; length++)
        {
            if (MatchesExactly(segments, length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Classic iterative wildcard match over the first <paramref name="length"/> segments.
    ///
    /// Iterative on purpose: nesting depth comes from the document being compared, so a recursive
    /// matcher would put an attacker-controlled depth on the stack - the same reason the JSON parser
    /// is iterative.
    /// </summary>
    private bool MatchesExactly(List<Segment> path, int length)
    {
        int pathIndex = 0, patternIndex = 0, starPattern = -1, starPath = 0;

        while (pathIndex < length)
        {
            if (patternIndex < _segments.Count && _segments[patternIndex].Kind == Kind.AnyDescendant)
            {
                starPattern = patternIndex;
                starPath = pathIndex;
                patternIndex++;
            }
            else if (patternIndex < _segments.Count && IsMatch(_segments[patternIndex], path[pathIndex]))
            {
                patternIndex++;
                pathIndex++;
            }
            else if (starPattern >= 0)
            {
                // Back up and let the descendant wildcard swallow one more segment.
                patternIndex = starPattern + 1;
                starPath++;
                pathIndex = starPath;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < _segments.Count && _segments[patternIndex].Kind == Kind.AnyDescendant)
        {
            patternIndex++;
        }

        return patternIndex == _segments.Count;
    }

    private static bool IsMatch(Segment pattern, Segment step) => pattern.Kind switch
    {
        Kind.Property => step.Kind == Kind.Property && string.Equals(pattern.Name, step.Name, StringComparison.Ordinal),
        Kind.Index => step.Kind == Kind.Index && pattern.Index == step.Index,
        Kind.AnyIndex => step.Kind == Kind.Index,
        _ => false,
    };

    /// <summary>Flattens a path into steps, root first. Walks up and reverses - no recursion.</summary>
    private static List<Segment> SegmentsOf(JsonPath path)
    {
        var segments = new List<Segment>();

        for (var node = path; node?.Parent is not null; node = node.Parent)
        {
            segments.Add(node.IsIndex
                ? new Segment(Kind.Index, null, ParseIndex(node.Label))
                : new Segment(Kind.Property, node.Label, -1));
        }

        segments.Reverse();
        return segments;
    }

    /// <summary>Label for an index step is "[7]"; -1 when it is somehow not, which then matches nothing.</summary>
    private static int ParseIndex(string label) =>
        label.Length > 2 && int.TryParse(label[1..^1], out var value) ? value : -1;

    private static string Normalize(List<Segment> segments)
    {
        var builder = new StringBuilder("$");

        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case Kind.Property:
                    builder.Append('.').Append(segment.Name);
                    break;
                case Kind.Index:
                    builder.Append('[').Append(segment.Index).Append(']');
                    break;
                case Kind.AnyIndex:
                    builder.Append("[*]");
                    break;
                case Kind.AnyDescendant:
                    builder.Append('.');
                    break;
            }
        }

        return builder.ToString();
    }

    public override string ToString() => Text;
}
