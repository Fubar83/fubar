using System;
using System.Threading.Tasks;
using Fubar.Controls;

namespace Fubar.Studio.UI.Services;

/// <summary>What to do about an editor that has unsaved changes.</summary>
public enum UnsavedChoice
{
    /// <summary>Go ahead - either it was saved, or the user chose to lose the edits.</summary>
    Proceed,

    /// <summary>Do not go ahead. Cancel, a dismissed dialog, no way to ask, or a save that failed.</summary>
    Keep,
}

/// <summary>
/// The decision behind "you have unsaved changes" - separated from the shell so the answers that
/// MATTER can be tested without a window, which is the same reasoning
/// <c>AuthorizationCodeFlow</c> gives for splitting its pure half out.
///
/// <para>The rule it exists to enforce: <b>anything that is not an explicit answer means keep.</b>
/// <see cref="IConfirmationService.ChooseAsync"/> returns -1 for a dismissed dialog and for having no
/// window to be modal to, and a save that fails leaves the editor dirty. All three are Keep. Treating
/// "went away" as consent to discard is precisely the bug a confirmation exists to prevent, and it is
/// reachable here in three different ways.</para>
/// </summary>
public static class UnsavedChangesPrompt
{
    /// <summary>
    /// Asks, and reports what to do.
    /// </summary>
    /// <param name="isDirty">Whether there is anything to lose. Clean editors are never asked about.</param>
    /// <param name="editorName">Shown in the question, so the user knows WHICH thing is unsaved.</param>
    /// <param name="confirmation">How to ask, or null when there is no way to - which is a Keep.</param>
    /// <param name="saveAsync">Performs the save and reports whether the editor came back clean.</param>
    public static async Task<UnsavedChoice> AskAsync(
        bool isDirty,
        string editorName,
        IConfirmationService? confirmation,
        Func<Task<bool>> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);

        if (!isDirty)
        {
            return UnsavedChoice.Proceed;
        }

        if (confirmation is null)
        {
            return UnsavedChoice.Keep;
        }

        // Save first: it is drawn as the primary button, and it is the only answer that loses nothing.
        var choice = await confirmation.ChooseAsync(
            $"Save changes to \"{editorName}\"?",
            "It has unsaved edits. Only one request is open at a time, so opening another one replaces it.",
            ["Save", "Discard"]);

        return choice switch
        {
            // A save that failed says so in the log and leaves the editor dirty; carrying on would
            // discard the very edits the user just asked to keep.
            0 => await saveAsync() ? UnsavedChoice.Proceed : UnsavedChoice.Keep,
            1 => UnsavedChoice.Proceed,
            _ => UnsavedChoice.Keep,
        };
    }
}
