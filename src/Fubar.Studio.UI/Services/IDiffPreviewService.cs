using System.Threading.Tasks;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Shows a side-by-side comparison of two pieces of text in a modal window.
///
/// Abstracted like the file picker and import dialog so view models can ask for a comparison without
/// depending on the view layer.
/// </summary>
public interface IDiffPreviewService
{
    /// <summary>
    /// Opens the comparison and returns when the user closes it. Purely informational - nothing is
    /// merged or saved, so there is no result to return.
    /// </summary>
    /// <param name="leftText">Left-hand content.</param>
    /// <param name="rightText">Right-hand content.</param>
    /// <param name="leftLabel">What the left side is, e.g. "In workspace".</param>
    /// <param name="rightLabel">What the right side is, e.g. "From spec".</param>
    /// <param name="title">Window title.</param>
    /// <param name="ignore">
    /// Ignore rules for this comparison, and optionally a way to persist them. Null when the
    /// comparison has nowhere to remember a rule, which also hides the affordance.
    /// </param>
    Task ShowAsync(
        string leftText,
        string rightText,
        string leftLabel,
        string rightLabel,
        string title,
        DiffIgnoreContext? ignore = null);
}
