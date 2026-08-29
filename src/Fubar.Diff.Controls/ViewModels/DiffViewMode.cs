using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Controls.ViewModels;

/// <summary>How the diff pane presents the comparison.</summary>
public enum DiffViewMode
{
    /// <summary>The two-editor side-by-side view. Always available, and the default for anything that is not JSON.</summary>
    SideBySide,

    /// <summary>
    /// One document, patch style: removals then additions, with shared context between them. Always
    /// available.
    ///
    /// Worth having alongside side-by-side rather than instead of it, because the two answer different
    /// questions. Side by side is better for comparing two versions of a line; unified is better on a
    /// narrow window, in a screenshot, and for anyone who reads patches all day - and it needs no
    /// horizontal space for a second column, which is most of why people prefer it.
    /// </summary>
    Unified,

    /// <summary>
    /// The change tree plus both documents, each shown as its own unaligned text with the current
    /// change's own span highlighted directly. Immune to formatting and property-order differences by
    /// construction: there is no cross-document line alignment to get confused by, since each side
    /// highlights its own <see cref="SourceSpan"/> independently. Only meaningful after a semantic
    /// comparison, which is also when it becomes the default - see <see cref="DiffPaneViewModel.Show"/>.
    /// </summary>
    Json,
}
