namespace Fubar.Studio.Core.Auth;

/// <summary>
/// PORT. Opens the user's browser at an authorize URL and waits for the provider to redirect back to
/// a loopback address.
///
/// A port because it is the only part of the authorization-code grant that needs a socket and a
/// browser, and because a view model must not own either. Everything that can be decided without them
/// - building the URL, checking the state, reading the code - is in
/// <see cref="AuthorizationCodeFlow"/>, pure and tested.
/// </summary>
public interface IAuthorizationCodeListener
{
    /// <summary>
    /// A free loopback port to advertise as the redirect. Asked for separately because the port is
    /// part of the redirect URI, which has to be REGISTERED with the provider before any of this can
    /// work - so the user needs to be told it before the flow runs.
    /// </summary>
    int ReservePort();

    /// <summary>
    /// Opens <paramref name="request"/>'s URL and waits for the redirect.
    ///
    /// Cancellation is the ordinary ending, not an exceptional one: the user may close the browser
    /// tab, sign in as the wrong person and give up, or simply walk away, and none of those should
    /// leave a listener holding a port for the life of the process.
    /// </summary>
    Task<AuthorizationCallback> ListenAsync(AuthorizationRequest request, CancellationToken cancellationToken = default);
}
