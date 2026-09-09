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
    /// <param name="settings">
    /// The comparison-settings hierarchy this comparison belongs to, and optionally a way to persist a
    /// change to it. Null when the comparison has nowhere to remember a setting, which also hides the
    /// affordances.
    /// </param>
    /// <param name="accept">
    /// How to write a difference INTO the left-hand side, when the left-hand side is something that
    /// can be rewritten - a snapshot. Null everywhere else, which hides the affordance rather than
    /// offering a button that cannot work.
    /// </param>
    Task ShowAsync(
        string leftText,
        string rightText,
        string leftLabel,
        string rightLabel,
        string title,
        DiffSettingsContext? settings = null,
        SnapshotAcceptContext? accept = null);
}

/// <summary>
/// Lets a comparison accept what it is showing into the recorded answer.
/// </summary>
/// <remarks>
/// <para>What makes a forty-difference wall workable: accept the three that were intended, and what
/// remains is the regression. Without it the only choice is re-record everything, which accepts the
/// regression along with them.</para>
/// <para>Never automatic and never bulk across steps: this is per row, from the pane that showed you
/// the difference, which is the only moment the answer is actually known.</para>
/// </remarks>
/// <param name="Description">What is being written, e.g. <c>staging.json</c>. Named on the button,
/// because accepting into a SHARED snapshot changes what every environment compares against.</param>
/// <param name="AcceptAsync">
/// Writes one path, or everything when the path is null, and returns the new left-hand text so the
/// pane can re-compare against what it just wrote. Null back means nothing was written.
/// </param>
public sealed record SnapshotAcceptContext(
    string Description,
    Func<string?, Task<string?>> AcceptAsync);
