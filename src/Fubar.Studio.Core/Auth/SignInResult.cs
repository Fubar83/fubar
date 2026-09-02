namespace Fubar.Studio.Core.Auth;

/// <summary>
/// The outcome of the browser half of an authorization-code sign-in.
/// </summary>
/// <param name="Ok">Whether a code came back and was stored.</param>
/// <param name="Message">What to tell the user - what happened, or why it did not.</param>
/// <param name="RedirectUri">
/// The loopback address that was listened on. Returned so the editor can show it: it must be
/// registered with the provider exactly as written, and a sign-in that fails because it was not is
/// the most opaque failure in this grant - the browser shows the provider's error page and the app
/// hears nothing at all.
/// </param>
public sealed record SignInResult(bool Ok, string Message, string? RedirectUri);
