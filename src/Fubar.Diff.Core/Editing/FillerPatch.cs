using System.Collections.Generic;

namespace Fubar.Diff.Core.Editing;

/// <summary>What a single step of a re-alignment does to the pane's document.</summary>
public enum FillerEditKind
{
    /// <summary>Insert a blank filler line at this line number, pushing everything below it down.</summary>
    InsertBlank,

    /// <summary>Remove the line at this line number.</summary>
    RemoveLine,
}

/// <summary>
/// One step of a re-alignment, in the document's own 1-based line numbers, to be applied in order.
/// </summary>
public readonly record struct FillerEdit(int LineNumber, FillerEditKind Kind);

/// <summary>
/// Works out the smallest set of changes that turns one alignment of a side into another.
///
/// This exists so that re-diffing after every edit does not mean handing the editor a whole new
/// document. Replacing the text would work and would also throw away the caret, the selection and the
/// undo history - three things the user is in the middle of using. Since the file's own lines are
/// identical on both sides of a re-alignment (the new alignment was computed FROM this document), the
/// only difference is where the blank fillers sit, and moving a few blank lines is a change small
/// enough to be invisible.
///
/// It refuses rather than guesses. If the two alignments differ by anything other than fillers, the
/// assumption behind the whole approach has been broken and the answer is null - the caller replaces
/// the document wholesale, losing the caret but never the content.
/// </summary>
public static class FillerPatch
{
    /// <summary>
    /// The edits that turn <paramref name="current"/> into <paramref name="wanted"/>, or null when the
    /// two do not differ by fillers alone.
    /// </summary>
    /// <param name="current">For each line of the document now, whether it is a filler.</param>
    /// <param name="wanted">For each line of the new alignment, whether it is a filler.</param>
    public static IReadOnlyList<FillerEdit>? Compute(IReadOnlyList<bool> current, IReadOnlyList<bool> wanted)
    {
        var edits = new List<FillerEdit>();

        var from = 0;
        var to = 0;

        // The line number as the document will look WHILE the edits are applied in order, which is not
        // the same as either input's indexing: a removal leaves the following line at the same number,
        // an insertion moves it down. Tracking it here is what lets the caller apply the list straight
        // through without recomputing anything.
        var line = 1;

        while (from < current.Count || to < wanted.Count)
        {
            var isFillerNow = from < current.Count && current[from];
            var wantFiller = to < wanted.Count && wanted[to];

            if (from < current.Count && to < wanted.Count && isFillerNow == wantFiller)
            {
                from++;
                to++;
                line++;

                continue;
            }

            if (isFillerNow)
            {
                edits.Add(new FillerEdit(line, FillerEditKind.RemoveLine));
                from++;

                continue;
            }

            if (wantFiller)
            {
                edits.Add(new FillerEdit(line, FillerEditKind.InsertBlank));
                to++;
                line++;

                continue;
            }

            // A real line on one side with no counterpart on the other. The premise does not hold, and
            // continuing would silently drop or duplicate the user's text - so say so instead.
            return null;
        }

        return edits;
    }

    /// <summary>
    /// Whether each line of an alignment is a filler, which is what <see cref="Compute"/> compares.
    /// A filler is a row this side has no line for, and carries no source number.
    /// </summary>
    public static IReadOnlyList<bool> FillerFlags(IReadOnlyList<Rendering.AlignedLine> lines)
    {
        var flags = new bool[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            flags[i] = lines[i].SourceNumber is null;
        }

        return flags;
    }

    /// <summary>The 1-based line numbers that are fillers, ascending - the form the caret mapping wants.</summary>
    public static IReadOnlyList<int> FillerLines(IReadOnlyList<bool> flags)
    {
        var lines = new List<int>();

        for (var i = 0; i < flags.Count; i++)
        {
            if (flags[i])
            {
                lines.Add(i + 1);
            }
        }

        return lines;
    }
}
