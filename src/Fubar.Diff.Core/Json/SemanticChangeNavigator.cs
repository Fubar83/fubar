using System.Collections.Generic;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Domain policy for "jump to the next/previous semantic difference" - the Hybrid view's navigation,
/// which steps through <see cref="JsonChange"/> entries directly rather than through
/// <see cref="Models.DiffHunk"/>s. It exists because the Hybrid view has no text alignment to hang
/// hunks off: each side shows its own document, unaligned, and highlights a change's own
/// <see cref="SourceSpan"/> directly - so navigation needs to walk the CHANGE LIST, not rows.
///
/// Mirrors <see cref="Comparison.HunkNavigator"/>'s wrap-around semantics exactly, and skips ignored
/// changes for the same reason hunk navigation skips ignored rows: they are not something the user
/// asked to see.
/// </summary>
public static class SemanticChangeNavigator
{
    /// <summary>
    /// The change index to move to from <paramref name="currentIndex"/>, wrapping past the end.
    /// Returns null when there is nothing navigable (no changes, or every change is ignored).
    /// </summary>
    public static int? Next(IReadOnlyList<JsonChange> changes, int currentIndex, bool includeIgnored = false)
    {
        var navigable = Navigable(changes, includeIgnored);
        if (navigable.Count == 0)
        {
            return null;
        }

        var position = navigable.IndexOf(currentIndex);
        return navigable[(position + 1) % navigable.Count];
    }

    /// <summary>
    /// The previous navigable index, wrapping past the start. From "none selected" (-1) this lands on
    /// the LAST navigable change, matching <see cref="Comparison.HunkNavigator.Previous"/>.
    /// </summary>
    public static int? Previous(IReadOnlyList<JsonChange> changes, int currentIndex, bool includeIgnored = false)
    {
        var navigable = Navigable(changes, includeIgnored);
        if (navigable.Count == 0)
        {
            return null;
        }

        var position = navigable.IndexOf(currentIndex);
        return position <= 0 ? navigable[^1] : navigable[position - 1];
    }

    /// <summary>
    /// Indices of changes that are real, navigable differences - ignored ones are skipped.
    ///
    /// Unless asked for: "what exactly am I not being told?" is a question worth a gesture of its own,
    /// usually asked right after adding a rule and once more before trusting the diff. That is
    /// Shift+Alt+Up/Down; ordinary Prev/Next still steps past them, which is what having rules is for.
    /// </summary>
    private static List<int> Navigable(IReadOnlyList<JsonChange> changes, bool includeIgnored)
    {
        var result = new List<int>();

        for (var i = 0; i < changes.Count; i++)
        {
            if (includeIgnored || !changes[i].IsIgnored)
            {
                result.Add(i);
            }
        }

        return result;
    }
}
