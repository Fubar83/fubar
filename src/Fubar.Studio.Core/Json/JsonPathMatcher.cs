namespace Fubar.Studio.Core.Json;

/// <summary>
/// Decides whether a concrete JSON path - <c>$.orders[2].total</c>, as a difference reports it -
/// is one a rule's pattern names.
/// </summary>
/// <remarks>
/// <para>The read-side counterpart of <c>JsonPathRewriter</c>, which walks a document applying a
/// pattern. Here there is no document: a difference already carries the exact path it happened at,
/// and the only question is whether a rule covers it. Same supported subset on purpose - <c>$</c>,
/// <c>.name</c>, <c>..name</c>, <c>[*]</c>, <c>[0]</c> - so a path written for redaction means the
/// same thing when written for a tolerance.</para>
/// <para>BCL only, like the rest of <c>Fubar.Studio.Core</c>: the diff engine has its own matcher,
/// and reaching for it here would drag the engine into Core to answer a question about a string.</para>
/// </remarks>
public static class JsonPathMatcher
{
    /// <summary>Whether <paramref name="pattern"/> names <paramref name="path"/>. An unparseable
    /// pattern matches nothing, rather than throwing - a bad rule must not take a run down.</summary>
    public static bool Matches(string pattern, string path)
    {
        if (!TryParsePattern(pattern, out var steps) || !TryParsePath(path, out var actual))
        {
            return false;
        }

        return Match(steps, 0, actual, 0);
    }

    /// <summary>The path with its last step removed, or null at the root. What tells an array's own
    /// path from the path of an element that was added to it.</summary>
    public static string? ParentOf(string path)
    {
        if (!TryParsePath(path, out var steps) || steps.Count == 0)
        {
            return null;
        }

        // Scanned forward rather than searched backwards: a bracket-quoted key may itself contain a
        // dot or a bracket, and the last one in the string is then inside the key rather than before
        // it. The parse above has already proved the shape, so this only has to find where the final
        // step starts.
        var start = 1;
        var i = 1;
        while (i < path.Length)
        {
            start = i;

            if (path[i] == '[')
            {
                i = path.IndexOf(']', i) + 1;
                continue;
            }

            i++;
            while (i < path.Length && path[i] is not ('.' or '['))
            {
                i++;
            }
        }

        return start <= 1 ? "$" : path[..start];
    }

    /// <summary>Whether the pattern is one this understands, so a caller can report a rule it is about
    /// to ignore instead of silently doing nothing.</summary>
    public static bool IsSupported(string pattern) => TryParsePattern(pattern, out _);

    private sealed record PatternStep(string? Name, bool Recursive, bool AllIndices, int? Index);

    /// <summary>One step of a CONCRETE path: a property name, or an array index when
    /// <see cref="Name"/> is null.</summary>
    public sealed record PathStep(string? Name, int Index);

    /// <summary>
    /// The steps of a concrete path - the kind a difference reports, with no wildcards in it.
    /// </summary>
    /// <remarks>
    /// Public so accepting a difference into a snapshot can address the same node the comparison
    /// named, using the same parse. A second walker over the same strings is a second set of rules
    /// about what <c>['a.b']</c> means.
    /// </remarks>
    public static IReadOnlyList<PathStep> Steps(string path) =>
        TryParsePath(path, out var steps) ? steps : [];

    private static bool Match(List<PatternStep> pattern, int p, List<PathStep> path, int c)
    {
        if (p == pattern.Count)
        {
            return c == path.Count;
        }

        var step = pattern[p];

        if (step.Name is { } name)
        {
            if (!step.Recursive)
            {
                return c < path.Count
                    && path[c].Name == name
                    && Match(pattern, p + 1, path, c + 1);
            }

            // A descendant step may skip any number of levels, so every later occurrence of the name
            // is a candidate and the first one that lets the REST of the pattern match wins.
            for (var i = c; i < path.Count; i++)
            {
                if (path[i].Name == name && Match(pattern, p + 1, path, i + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (c >= path.Count || path[c].Name is not null)
        {
            return false;
        }

        return (step.AllIndices || step.Index == path[c].Index)
            && Match(pattern, p + 1, path, c + 1);
    }

    private static bool TryParsePattern(string pattern, out List<PatternStep> steps)
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

                steps.Add(new PatternStep(pattern[start..i], recursive, false, null));
                continue;
            }

            if (pattern[i] != '[')
            {
                return false;
            }

            var close = pattern.IndexOf(']', i);
            if (close < 0)
            {
                return false;
            }

            var inner = pattern[(i + 1)..close];
            i = close + 1;

            if (inner == "*")
            {
                steps.Add(new PatternStep(null, false, true, null));
            }
            else if (int.TryParse(inner, out var index))
            {
                steps.Add(new PatternStep(null, false, false, index));
            }
            else if (inner.Length >= 2 && inner[0] == '\'' && inner[^1] == '\'')
            {
                steps.Add(new PatternStep(Unquote(inner), false, false, null));
            }
            else
            {
                return false;
            }
        }

        return steps.Count > 0;
    }

    /// <summary>Parses what <c>JsonPath.ToString()</c> writes: <c>$</c>, <c>.name</c>, <c>[0]</c> and
    /// the bracket-quoted form it falls back to for a key containing a dot or a space.</summary>
    private static bool TryParsePath(string path, out List<PathStep> steps)
    {
        steps = [];
        if (string.IsNullOrWhiteSpace(path) || path[0] != '$')
        {
            return false;
        }

        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;
                var start = i;
                while (i < path.Length && path[i] is not ('.' or '['))
                {
                    i++;
                }

                if (i == start)
                {
                    return false;
                }

                steps.Add(new PathStep(path[start..i], -1));
                continue;
            }

            if (path[i] != '[')
            {
                return false;
            }

            var close = path.IndexOf(']', i);
            if (close < 0)
            {
                return false;
            }

            var inner = path[(i + 1)..close];
            i = close + 1;

            if (int.TryParse(inner, out var index))
            {
                steps.Add(new PathStep(null, index));
            }
            else if (inner.Length >= 2 && inner[0] == '\'' && inner[^1] == '\'')
            {
                steps.Add(new PathStep(Unquote(inner), -1));
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static string Unquote(string quoted) =>
        quoted[1..^1].Replace("\'", "'", StringComparison.Ordinal);
}
