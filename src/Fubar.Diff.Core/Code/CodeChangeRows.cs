using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Code;

/// <summary>
/// Turns a structural change into the ALIGNED ROW that shows it, so clicking a member in the
/// structure panel scrolls the text view to that member.
///
/// The translation is needed because the two halves address the file differently and both are right:
/// a <see cref="CodeChange"/> carries a <see cref="Json.SourceSpan"/> into one side's own text, while
/// every view here is indexed by row of the shared, filler-padded alignment. Everything else that
/// bridges the two - <c>JsonChangeLines</c>, the diff map - does the same thing for its own case;
/// this is that rule for members.
/// </summary>
public static class CodeChangeRows
{
    /// <summary>
    /// The row a change should scroll to, or -1 when it cannot be placed.
    ///
    /// The RIGHT side is preferred, because it is the file as it now is and the one a reader is
    /// deciding about; a removal has no right side, so it falls back to where the member used to be.
    /// A member that was only moved has both, and the right is still the honest answer to "take me to
    /// it".
    /// </summary>
    public static int RowFor(DiffResult result, CodeChange change)
    {
        if (change.Right is { Span.IsKnown: true } right && RowOf(result.Lines, right.Span.StartLine, left: false) is var row and >= 0)
        {
            return row;
        }

        return change.Left is { Span.IsKnown: true } left
            ? RowOf(result.Lines, left.Span.StartLine, left: true)
            : -1;
    }

    /// <summary>
    /// The row carrying a given 1-based source line of one side.
    ///
    /// A scan rather than an index, deliberately: this runs on a click, once, and building a
    /// line-to-row map for a document that may have a million rows in order to answer one question is
    /// the more expensive choice by a wide margin.
    /// </summary>
    private static int RowOf(IReadOnlyList<DiffLine> lines, int sourceLine, bool left)
    {
        for (var row = 0; row < lines.Count; row++)
        {
            var number = left ? lines[row].LeftNumber : lines[row].RightNumber;

            if (number == sourceLine)
            {
                return row;
            }
        }

        return -1;
    }
}
