using System.Collections.Generic;
using System.Linq;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// A set of paths whose differences are never reported.
///
/// The problem it solves: a response full of <c>requestId</c>, <c>timestamp</c> and <c>traceId</c>
/// differs on every single call, so comparing two runs shows a wall of changes with the one that
/// matters buried in it. Ignoring those paths is what makes the comparison usable.
///
/// Applied by dropping changes before the line filter runs, so an ignored path is not merely hidden
/// from the tree - the rows stop counting as changes in the text view, the diff map and navigation
/// too. Anything else would give the two views different answers.
/// </summary>
public sealed class JsonIgnoreRules
{
    /// <summary>Ignores nothing.</summary>
    public static JsonIgnoreRules None { get; } = new([]);

    private readonly IReadOnlyList<JsonPathPattern> _patterns;

    private JsonIgnoreRules(IReadOnlyList<JsonPathPattern> patterns) => _patterns = patterns;

    public bool IsEmpty => _patterns.Count == 0;

    /// <summary>
    /// Builds a rule set, discarding anything that does not parse.
    ///
    /// Silently, on purpose: these are persisted in a hand-editable request file, and refusing to
    /// compare a response because one rule has a typo would be a worse failure than quietly applying
    /// the rules that do work.
    /// </summary>
    public static JsonIgnoreRules From(IEnumerable<string>? paths)
    {
        if (paths is null)
        {
            return None;
        }

        var patterns = new List<JsonPathPattern>();

        foreach (var path in paths)
        {
            if (JsonPathPattern.TryParse(path, out var pattern) && pattern is not null)
            {
                patterns.Add(pattern);
            }
        }

        return patterns.Count == 0 ? None : new JsonIgnoreRules(patterns);
    }

    /// <summary>True when any rule covers this path.</summary>
    public bool IsIgnored(JsonPath path) => _patterns.Any(p => p.Matches(path));

    /// <summary>
    /// The same changes, with the covered ones flagged <see cref="JsonChange.IsIgnored"/>.
    ///
    /// Flagged rather than removed. An ignored difference still exists, and rendering nothing where
    /// one is would leave the user unable to tell "these are the same" from "this is being ignored" -
    /// which matters most right after adding a rule, when what they want to confirm is that it hid
    /// the field they meant. Everything downstream drops them from counts, hunks and navigation; only
    /// the renderers still know they are there.
    /// </summary>
    public IReadOnlyList<JsonChange> Mark(IReadOnlyList<JsonChange> changes)
    {
        if (IsEmpty || changes.Count == 0)
        {
            return changes;
        }

        return changes
            .Select(c => IsIgnored(c.Path) ? c with { IsIgnored = true } : c)
            .ToList();
    }
}
