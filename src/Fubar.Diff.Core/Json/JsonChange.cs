using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// One semantic difference between two JSON documents.
///
/// Carries both nodes rather than just their text so the UI can show the values, and so the
/// <see cref="SourceSpan"/> on each is available to highlight the change in the corresponding editor -
/// which is what lets a tree-based diff render itself over a text view.
/// </summary>
/// <param name="Path">Where in the document, e.g. <c>$.users[2].email</c>.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Left">The left-hand node, or null when the value was added.</param>
/// <param name="Right">The right-hand node, or null when the value was removed.</param>
public sealed record JsonChange(
    JsonPath Path,
    ChangeKind Kind,
    JsonAstNode? Left,
    JsonAstNode? Right)
{
    /// <summary>
    /// Set when the change is a property whose NAME is what identifies it - so the renderer can
    /// highlight the key as well as the value. Null for array elements and the document root.
    /// </summary>
    public SourceSpan LeftNameSpan { get; init; }

    /// <summary>The matching name span on the right.</summary>
    public SourceSpan RightNameSpan { get; init; }

    /// <summary>
    /// Everything this change covers on the left - what a renderer should highlight.
    ///
    /// For a property that was ADDED or REMOVED that is the whole <c>"name": value</c> pair, because
    /// the whole pair is what appeared or went away; highlighting the value alone leaves the key
    /// looking untouched next to it, which is precisely the wrong reading. For a value that merely
    /// CHANGED it is the value alone: the key is still there, still spelled the same, and colouring it
    /// would claim an edit nobody made.
    ///
    /// A property that only MOVED counts with the first group - the pair went somewhere else as a
    /// unit.
    /// </summary>
    public SourceSpan LeftSpan => Covered(Left, LeftNameSpan);

    /// <summary>Everything this change covers on the right. See <see cref="LeftSpan"/>.</summary>
    public SourceSpan RightSpan => Covered(Right, RightNameSpan);

    private SourceSpan Covered(JsonAstNode? node, SourceSpan nameSpan)
    {
        var value = node?.Span ?? SourceSpan.None;

        return Kind is ChangeKind.Inserted or ChangeKind.Deleted || IsReorder
            ? nameSpan.Union(value)
            : value;
    }

    /// <summary>
    /// True when this is a property that only moved. Reported solely when
    /// <see cref="JsonComparisonOptions.ReportPropertyOrder"/> is on, and kept distinguishable so the
    /// UI can present it differently from a value that actually changed.
    /// </summary>
    public bool IsReorder { get; init; }

    /// <summary>
    /// True when an ignore rule covers this path.
    ///
    /// Marked rather than removed: an ignored difference still EXISTS, and a comparison that renders
    /// nothing at all where one is would leave the user unable to tell "this field is the same" from
    /// "this field is being ignored". It is excluded from the counts, the hunks and navigation, and
    /// drawn only as a faint band.
    /// </summary>
    public bool IsIgnored { get; init; }

    public override string ToString() => $"{Kind} at {Path}";
}
