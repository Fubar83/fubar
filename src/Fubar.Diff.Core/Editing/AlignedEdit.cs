using System.Collections.Generic;

namespace Fubar.Diff.Core.Editing;

/// <summary>
/// Translating between what a diff pane SHOWS and what the file actually contains.
///
/// The pane's document is the file with blank filler rows interleaved, so that both columns have the
/// same number of lines (see <c>AlignedText</c>). Once that document is editable, everything hinges on
/// being able to take it back apart afterwards - and doing so without a general offset map, which is
/// what made this look expensive for a long time.
///
/// The rule is one line long: <b>a line belongs to the file unless it is empty AND is still a
/// filler.</b> Which lines are still fillers is answered by the editor itself, through anchors that
/// move with the text; this class does not track them, it just applies the rule.
///
/// What makes the rule work rather than merely plausible is what it does with the awkward cases.
/// Typing into a filler makes it non-empty, so it becomes a real line - which is exactly what the user
/// meant: adding a line where the other side already had one. A blank line the user types is not a
/// filler, has no anchor, and is kept. Deleting across a filler destroys its anchor, so the text that
/// replaces it is kept whole.
/// </summary>
public static class AlignedEdit
{
    /// <summary>
    /// The file's lines, given the pane's current document lines and the line numbers that are still
    /// fillers.
    /// </summary>
    /// <param name="documentLines">Every line the editor currently holds, in order.</param>
    /// <param name="fillerLines">1-based line numbers still carrying a live filler anchor.</param>
    public static IReadOnlyList<string> ToFileLines(
        IReadOnlyList<string> documentLines,
        IReadOnlySet<int> fillerLines)
    {
        var lines = new List<string>(documentLines.Count);

        for (var i = 0; i < documentLines.Count; i++)
        {
            // 1-based, to match how the editor numbers its own lines - this is the only place the two
            // conventions meet, so the conversion lives here rather than at every call site.
            if (documentLines[i].Length == 0 && fillerLines.Contains(i + 1))
            {
                continue;
            }

            lines.Add(documentLines[i]);
        }

        return lines;
    }

    /// <summary>
    /// Which line of the FILE a given document line is, counting from 1.
    ///
    /// A caret sitting on a filler has no file line of its own, and reports the line it would push
    /// down - the next real one. That is the right answer for the only thing this is used for, which
    /// is putting the caret back after the fillers have moved: landing just above the following line
    /// is where the user was pointing.
    /// </summary>
    public static int ToFileLine(int documentLine, IReadOnlySet<int> fillerLines)
    {
        var fileLine = 1;

        for (var line = 1; line < documentLine; line++)
        {
            if (!fillerLines.Contains(line))
            {
                fileLine++;
            }
        }

        return fileLine;
    }

    /// <summary>
    /// The document line showing a given file line, counting from 1 - the reverse of
    /// <see cref="ToFileLine"/> against a DIFFERENT set of fillers, which is the whole point: the
    /// alignment either side of an edit is not the same alignment.
    /// </summary>
    /// <param name="fileLine">1-based line in the file.</param>
    /// <param name="fillerLines">1-based filler line numbers in the new document, ascending.</param>
    /// <param name="documentLineCount">Total lines in the new document, to clamp against.</param>
    public static int ToDocumentLine(int fileLine, IReadOnlyList<int> fillerLines, int documentLineCount)
    {
        var documentLine = fileLine;

        // Every filler at or above the target pushes it down one. Ascending order means one pass:
        // a filler beyond the running answer cannot be above it, and neither can any after it.
        foreach (var filler in fillerLines)
        {
            if (filler <= documentLine)
            {
                documentLine++;
            }
            else
            {
                break;
            }
        }

        return documentLine < 1 ? 1 : documentLine > documentLineCount ? documentLineCount : documentLine;
    }
}
