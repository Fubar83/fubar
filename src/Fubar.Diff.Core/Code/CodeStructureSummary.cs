using System.Collections.Generic;
using System.Text;

namespace Fubar.Diff.Core.Code;

/// <summary>
/// The headline a structural comparison can give and a line-based one cannot: how much of what
/// changed actually matters.
///
/// The answer worth the whole feature is <see cref="NoFunctionalChange"/>. Two files that differ on
/// hundreds of lines because someone ran a formatter, moved three methods and rewrapped the comments
/// look exactly like two files with a bug fixed in them, and today the only way to tell is to read
/// every hunk. Saying it in one sentence is the difference between a review that takes a minute and
/// one nobody does properly.
/// </summary>
public sealed record CodeStructureSummary(
    int Added,
    int Removed,
    int Modified,
    int Renamed,
    int Cosmetic,
    int Moved)
{
    /// <summary>Nothing was compared - not source, did not parse, or turned off.</summary>
    public static CodeStructureSummary None { get; } = new(0, 0, 0, 0, 0, 0);

    /// <summary>Counts a set of changes.</summary>
    public static CodeStructureSummary Of(IReadOnlyList<CodeChange> changes)
    {
        int added = 0, removed = 0, modified = 0, renamed = 0, cosmetic = 0, moved = 0;

        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case CodeChangeKind.Added: added++; break;
                case CodeChangeKind.Removed: removed++; break;
                case CodeChangeKind.Modified: modified++; break;
                case CodeChangeKind.Renamed: renamed++; break;
                case CodeChangeKind.Cosmetic: cosmetic++; break;
            }

            // Counted from the FLAG, not the kind, so a method that was rewritten and moved is counted
            // in both places - which is what makes "3 changed, 2 moved" add up to the file people are
            // looking at rather than to a partition of it.
            if (change.IsMoved)
            {
                moved++;
            }
        }

        return new CodeStructureSummary(added, removed, modified, renamed, cosmetic, moved);
    }

    /// <summary>How many members changed in a way that changes what the file does.</summary>
    public int Functional => Added + Removed + Modified + Renamed;

    /// <summary>How many changed only in how they read.</summary>
    public int Presentational => Cosmetic + Moved;

    /// <summary>Whether anything at all was reported.</summary>
    public bool Any => Functional + Presentational > 0;

    /// <summary>
    /// True when the two files differ only in formatting, comments and declaration order.
    ///
    /// Deliberately requires that SOMETHING was reported: two identical files are identical, and
    /// saying "no functional changes" about them would be technically true and read as though a
    /// difference had been found and dismissed.
    /// </summary>
    public bool NoFunctionalChange => Functional == 0 && Presentational > 0;

    /// <summary>
    /// A sentence for the status bar. Empty when there is nothing to say.
    /// </summary>
    public string Caption()
    {
        if (!Any)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        Add(parts, Added, "added");
        Add(parts, Removed, "removed");
        Add(parts, Modified, "changed");
        Add(parts, Renamed, "renamed");
        Add(parts, Cosmetic, "reformatted");
        Add(parts, Moved, "moved");

        var text = new StringBuilder();

        if (NoFunctionalChange)
        {
            text.Append("No functional changes - ");
        }

        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                text.Append(i == parts.Count - 1 ? " and " : ", ");
            }

            text.Append(parts[i]);
        }

        return text.ToString();
    }

    private static void Add(List<string> parts, int count, string what)
    {
        if (count > 0)
        {
            parts.Add($"{count} {what}");
        }
    }
}
