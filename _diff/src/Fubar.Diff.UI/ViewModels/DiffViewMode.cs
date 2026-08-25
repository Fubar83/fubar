namespace Fubar.Diff.UI.ViewModels;

/// <summary>How the diff pane presents the comparison.</summary>
public enum DiffViewMode
{
    /// <summary>The two-editor side-by-side view. Always available.</summary>
    Text,

    /// <summary>
    /// The semantic change tree. Only meaningful after a semantic comparison has run - for a plain
    /// text file there are no structural changes to show.
    /// </summary>
    Tree,
}
