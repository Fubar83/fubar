using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Paints the whole-line change tint behind the text: the addition colour for an inserted row, the
/// removal colour for a deleted one, whichever of those a modified row's own side calls for (see
/// <see cref="TintKind"/>), and a dimmed band for filler rows. Every changed row gets one - quietly
/// when it is not the difference being read, more strongly when it is.
///
/// Draws on <see cref="KnownLayer.Background"/> so the tint sits under both the text and the selection
/// highlight; painting above would wash out the selection and make selected text unreadable.
///
/// Only the visible lines are drawn (AvaloniaEdit hands us exactly those), so cost is proportional to
/// the viewport rather than the document - which is the whole reason for moving to a real editor.
/// </summary>
internal sealed class ChangeLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly Avalonia.StyledElement _host;
    private IReadOnlyList<AlignedLine> _lines = [];
    private bool _emphasized;
    private int _currentStart = -1;
    private int _currentEnd = -1;
    private DiffSide? _side;

    public ChangeLineBackgroundRenderer(Avalonia.StyledElement host) => _host = host;

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Swaps in the metadata for a new comparison. The caller must redraw the text view.</summary>
    public void SetLines(IReadOnlyList<AlignedLine> lines) => _lines = lines;

    /// <summary>Whether this pane is a close-up (DiffDetailPane), where the tint should carry more weight.</summary>
    public void SetEmphasized(bool value) => _emphasized = value;

    /// <summary>
    /// Which side of the comparison this pane shows, or null for a pane that is neither (the unified
    /// view, a three-way base column). Only <see cref="TintKind"/> reads it - see there for why.
    /// </summary>
    public void SetSide(DiffSide? side) => _side = side;

    /// <summary>
    /// The current hunk's row range, so rows OUTSIDE it can fade - a difference elsewhere in the file
    /// should read as "there, but not what you're looking at" rather than compete equally with the one
    /// just navigated to. A negative start (nothing selected yet, or this is a close-up pane that never
    /// calls this) means nothing fades - everything shows at normal strength.
    /// </summary>
    public void SetCurrentRange(int startIndex, int endIndex)
    {
        _currentStart = startIndex;
        _currentEnd = endIndex;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        // The close-up panes (DiffDetailPane) skip the full-width band entirely: it is a page full of
        // nothing BUT the current difference, so a band across the whole pane width says nothing a
        // border around the pane does not already say. CharSpanColorizer carries the whole signal
        // there instead, precisely over the characters that changed rather than the row they sit on.
        if (_emphasized || _lines.Count == 0)
        {
            return;
        }

        textView.EnsureVisualLines();

        foreach (var visualLine in textView.VisualLines)
        {
            // AvaloniaEdit line numbers are 1-based; AlignedText is indexed from 0. A document can
            // briefly be longer than the metadata while a new comparison is being applied, so guard
            // rather than trusting them to be in step.
            var index = visualLine.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= _lines.Count)
            {
                continue;
            }

            // Both flags are checked BEFORE the by-kind lookup, and for opposite reasons. An ignored
            // row's Kind is Unchanged - it was downgraded so it forms no hunk - so falling through
            // would return no tint at all. A conflicting row's Kind is a perfectly ordinary
            // Inserted/Deleted, so falling through would tint it exactly like the changes that need
            // no decision, which is the one thing a merge view must not do.
            // A moved row is checked here for the same reason as a conflicting one: its Kind is an
            // ordinary Inserted/Deleted, so falling through would paint the two halves of one moved
            // block in the two colours that mean "written" and "removed" - which is exactly the
            // reading the mark exists to correct.
            var line = _lines[index];
            var brushOrNull = line.IsConflict
                ? DiffLineColors.ConflictBackground(_host, Emphasis(index))
                : line.IsIgnored
                    ? DiffLineColors.IgnoredBackground(_host)
                    : line.IsMoved
                        ? DiffLineColors.MovedBackground(_host, Emphasis(index))
                        : DiffLineColors.LineBackground(_host, TintKind(line), Emphasis(index));

            if (brushOrNull is not { } brush)
            {
                continue;
            }

            // Full viewport width, not just the text extent: a tint that stops at the end of the line
            // makes the changed block look ragged and is much harder to scan down.
            var top = visualLine.VisualTop - textView.VerticalOffset;
            drawingContext.FillRectangle(
                brush,
                new Rect(0, top, textView.Bounds.Width, visualLine.Height));
        }
    }

    /// <summary>
    /// How loudly a row draws: quietly unless it is part of the difference being read.
    ///
    /// Note what happens with NOTHING selected - every row fades. That is the point of tinting all of
    /// them: a document nobody has navigated yet should read as "here is where the changes are", one
    /// even wash, with nothing pretending to be the current one. Drawing them all at full strength
    /// instead (which is what a negative range used to mean) turns a heavily-edited file into a wall
    /// of colour, and leaves the first press of F8 with nothing to make stand out.
    /// </summary>
    private DiffEmphasis Emphasis(int index) =>
        _currentStart >= 0 && index >= _currentStart && index <= _currentEnd
            ? DiffEmphasis.Normal
            : DiffEmphasis.Faded;

    /// <summary>
    /// Which colour a row's tint should take.
    ///
    /// Everything but Modified answers for itself. A modified row is the same kind on BOTH sides -
    /// that is what "modified" means - so tinting it by kind alone would paint the two columns the
    /// same colour and say nothing about which one lost text and which one gained it. The side does:
    /// the left document lost something here, the right gained something, and those are the two
    /// colours the words inside the row are already highlighted in.
    ///
    /// Deriving it from the row's own spans instead was tried and is wrong: a line that only had text
    /// ADDED to it has no deleted spans on the left at all, so half the modified rows in an ordinary
    /// diff fell through to the fallback and came out a third colour. The side is never ambiguous.
    ///
    /// The fallback remains for a pane that is neither side - the unified view, a three-way base
    /// column - where a modified row would have no side to take a colour from. Neither of those
    /// actually produces one today (see <c>ThreeWayAlignedText.KindFor</c>), so this is a safety net
    /// rather than a case to design around.
    /// </summary>
    internal ChangeKind TintKind(AlignedLine line) => TintKind(line.Kind, _side);

    internal static ChangeKind TintKind(ChangeKind kind, DiffSide? side) => (kind, side) switch
    {
        (ChangeKind.Modified, DiffSide.Left) => ChangeKind.Deleted,
        (ChangeKind.Modified, DiffSide.Right) => ChangeKind.Inserted,
        _ => kind,
    };
}
