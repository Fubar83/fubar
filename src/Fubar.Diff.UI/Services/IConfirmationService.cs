using System.Threading.Tasks;

namespace Fubar.Diff.UI.Services;

/// <summary>
/// Asks the user to confirm something before it happens.
///
/// An interface for the usual reason - a view model must stay testable and must not reach for a
/// window - but also because a test that asserts "nothing was copied when the user said no" needs to
/// be able to say no.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Puts the question to the user and waits. Returns true only for an explicit yes: dismissing the
    /// dialog, closing it, or having no window to show it in all mean no.
    /// </summary>
    /// <param name="title">A short heading, e.g. "Replace 3 files?".</param>
    /// <param name="message">The detail, including the paths involved.</param>
    /// <param name="confirmLabel">The wording on the button that goes ahead, e.g. "Replace".</param>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);
}
