namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One field of a token response, as the auth editor offers it: what it is, where it would be saved,
/// and whether that has already been done.
///
/// <para>Exists so the button can name its own effect. It used to read "Capture" - this codebase's
/// word, not anyone else's, and one that says nothing about where the value goes or what to do with
/// it next. It reads "Save as {{oauth2_access_token}}" now, which is the same variable the
/// <c>Authorization: Bearer</c> line at the top of the screen shows, so the connection between the
/// two is on the button rather than in a paragraph above it.</para>
/// </summary>
/// <param name="Path">The JSONPath into the response, taken from a response that actually arrived.</param>
/// <param name="Preview">The value, shortened and with anything token-shaped masked.</param>
/// <param name="Variable">The session variable this would be saved into.</param>
/// <param name="IsCaptured">
/// Whether a rule already saves this path. The button used to be offered either way and silently do
/// nothing on the second click, which reads as a broken button rather than as "already done".
/// </param>
public sealed record CapturableField(string Path, string Preview, string Variable, bool IsCaptured)
{
    /// <summary>What the button says, naming the variable the value lands in.</summary>
    public string ActionLabel => $"Save as {{{{{Variable}}}}}";

    /// <summary>What replaces the button once there is nothing left to do.</summary>
    public string CapturedLabel => $"Saved as {{{{{Variable}}}}}";
}
