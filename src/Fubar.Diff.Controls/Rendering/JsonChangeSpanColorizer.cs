using System.Collections.Generic;
using Avalonia;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Marks EVERY change in a Json document, not only the one being read.
///
/// The Json view used to highlight the current change and nothing else, which made the two documents
/// beside the tree unreadable as documents: a file with eleven differences showed one, and the only
/// way to find out where the other ten were was to press Next eleven times. Each change now carries a
/// quiet tint of its own, and the current one is painted at full strength on top of the band and
/// accent bar <see cref="CurrentHunkRenderer"/> draws for it - the same "all of them softly, this one
/// clearly" arrangement the text views get from <see cref="ChangeLineBackgroundRenderer"/> and
/// <see cref="CharSpanColorizer"/>.
///
/// Character spans rather than full-width bands, unlike the text views, because a Json document is
/// not aligned: one line routinely holds several properties, and banding the line would claim the
/// whole of <c>{"a": 1, "b": 2}</c> changed when only <c>b</c> did.
/// </summary>
internal sealed class JsonChangeSpanColorizer : DocumentColorizingTransformer
{
    private readonly StyledElement _host;
    private IReadOnlyList<JsonChange> _changes = [];
    private DiffSide _side = DiffSide.Left;
    private SourceSpan? _current;

    public JsonChangeSpanColorizer(StyledElement host) => _host = host;

    /// <summary>
    /// Swaps in the changes to mark and which side of the comparison this pane shows. The caller must
    /// redraw.
    ///
    /// These must be the changes whose spans point into the text this pane is DISPLAYING - i.e. the
    /// "original" change list, addressing each side's raw text - not the copy addressing the
    /// canonicalized text the aligner worked on. See <c>DiffPaneViewModel.SemanticChanges</c>.
    /// </summary>
    public void SetChanges(IReadOnlyList<JsonChange> changes, DiffSide side)
    {
        _changes = changes;
        _side = side;
    }

    /// <summary>The change being read, painted at full strength. Null while nothing is selected.</summary>
    public void SetCurrent(SourceSpan? current) => _current = current;

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var change in _changes)
        {
            var span = _side == DiffSide.Left ? change.LeftSpan : change.RightSpan;

            // A change with nothing on this side - a property that was only added, seen from the left -
            // has no span here to paint, which is the whole of what "added" looks like on this side.
            if (SpanRange.Within(line, span) is not { } range
                || BrushFor(change, span) is not { } brush)
            {
                continue;
            }

            ChangeLinePart(
                line.Offset + range.Start,
                line.Offset + range.End,
                element => element.TextRunProperties.SetBackgroundBrush(brush));
        }
    }

    private Avalonia.Media.IBrush? BrushFor(JsonChange change, SourceSpan span)
    {
        // An ignored change is still drawn - the user asked not to be TOLD about it, not to have it
        // hidden - but in the neutral, barely-there colour the aligned views use for the same case, so
        // it cannot be mistaken for something that was reported.
        if (change.IsIgnored)
        {
            return DiffLineColors.IgnoredSpanBackground(_host);
        }

        return DiffLineColors.SpanBackground(_host, KindFor(change.Kind, _side), EmphasisFor(span, _current));
    }

    /// <summary>
    /// Which colour a change takes on this side.
    ///
    /// Modified means both documents have the property with different values, so the kind alone says
    /// nothing about which one you are looking at. The side does: what the left lost is a removal,
    /// what the right gained is an addition - the same reading, and the same two colours, the aligned
    /// views give a modified row (see <c>ChangeLineBackgroundRenderer.TintKind</c>).
    /// </summary>
    internal static ChangeKind KindFor(ChangeKind kind, DiffSide side) => kind == ChangeKind.Modified
        ? (side == DiffSide.Left ? ChangeKind.Deleted : ChangeKind.Inserted)
        : kind;

    /// <summary>
    /// How loudly to draw it: every change is marked, the one being read is marked clearly.
    ///
    /// Compared by span rather than by identity because that is what the pane is given - the current
    /// change arrives as the span to highlight, not as the change object it came from.
    /// </summary>
    internal static DiffEmphasis EmphasisFor(SourceSpan span, SourceSpan? current) =>
        current is { } known && known.Equals(span) ? DiffEmphasis.Normal : DiffEmphasis.Faded;
}
