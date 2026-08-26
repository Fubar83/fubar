using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Controls.ViewModels;

/// <summary>How the diff pane presents the comparison.</summary>
public enum DiffViewMode
{
    /// <summary>The two-editor side-by-side view. Always available.</summary>
    Text,

    /// <summary>
    /// The change tree plus both documents, each shown as its own unaligned text with the current
    /// change's own span highlighted directly. Immune to formatting and property-order differences by
    /// construction: there is no cross-document line alignment to get confused by, since each side
    /// highlights its own <see cref="SourceSpan"/> independently. Only meaningful after a semantic
    /// comparison, which is also when it becomes the default - see <see cref="DiffPaneViewModel.Show"/>.
    /// </summary>
    Json,
}
