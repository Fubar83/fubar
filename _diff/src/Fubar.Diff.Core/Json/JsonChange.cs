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
    /// True when this is a property that only moved. Reported solely when
    /// <see cref="JsonComparisonOptions.ReportPropertyOrder"/> is on, and kept distinguishable so the
    /// UI can present it differently from a value that actually changed.
    /// </summary>
    public bool IsReorder { get; init; }

    public override string ToString() => $"{Kind} at {Path}";
}
