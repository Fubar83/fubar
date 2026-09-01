namespace Fubar.Diff.Controls.ViewModels;

/// <summary>
/// How the diff pane lays out a TEXT comparison.
///
/// There is deliberately no Json member. Whether the Json view is shown is decided by how the files
/// are being COMPARED - the Auto/Text/Json selector - and having it here as well meant two controls
/// answering the same question, where picking Text in one and Json in the other was a contradiction
/// the app had to resolve behind the user's back. The comparison mode decides what is shown; this
/// decides how the text is laid out when text is what is shown.
/// </summary>
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
}
