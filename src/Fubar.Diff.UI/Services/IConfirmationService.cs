using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fubar.Diff.UI.Services;

/// <summary>
/// Puts a question to the user and waits for the answer.
///
/// An interface for the usual reason - a view model must stay testable and must not reach for a
/// window - but also because the tests that matter most here are about REFUSALS, and a test that
/// asserts "nothing was written when the user said no" needs to be able to say no.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Asks a yes/no question. Returns true only for an explicit yes: dismissing the dialog, closing
    /// it, or having no window to show it in all mean no.
    /// </summary>
    /// <param name="title">A short heading, e.g. "Replace 3 files?".</param>
    /// <param name="message">The detail, including the paths involved.</param>
    /// <param name="confirmLabel">The wording on the button that goes ahead, e.g. "Replace".</param>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);

    /// <summary>
    /// Asks the user to pick one of several answers, returning its index - or -1 when they picked
    /// none of them, which every caller must treat as "do nothing".
    ///
    /// Order the choices with the SAFEST first: it is the one drawn as primary, and the one a stray
    /// keypress is most likely to land on.
    /// </summary>
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices);

    /// <summary>
    /// Asks for a line of text, returning null when the user cancels or types nothing.
    ///
    /// Null rather than an empty string, so "cancelled" and "cleared it deliberately" stay
    /// distinguishable at the call site even where they happen to do the same thing.
    /// </summary>
    Task<string?> AskForTextAsync(string title, string message, string initial = "");
}
