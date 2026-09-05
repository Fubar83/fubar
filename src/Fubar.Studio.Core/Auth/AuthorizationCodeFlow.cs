using System.Collections.Specialized;
using System.Web;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// What an authorization-code attempt needs to remember between opening the browser and the redirect
/// coming back.
/// </summary>
/// <param name="AuthorizeUrl">The URL to open.</param>
/// <param name="RedirectUri">The loopback address the provider will send the browser back to.</param>
/// <param name="State">The value that must come back for the callback to be ours.</param>
/// <param name="Verifier">The PKCE secret, presented only in the token exchange.</param>
public sealed record AuthorizationRequest(string AuthorizeUrl, string RedirectUri, string State, string Verifier);

/// <summary>What came back on the redirect.</summary>
/// <param name="Code">The authorization code, when the user approved.</param>
/// <param name="Error">The provider's error code, when they did not.</param>
/// <param name="ErrorDescription">Its description, if any.</param>
public sealed record AuthorizationCallback(string? Code, string? Error, string? ErrorDescription)
{
    public bool Ok => !string.IsNullOrEmpty(Code) && Error is null;
}

/// <summary>
/// The pure half of the authorization-code grant: building the URL to open, and reading the redirect
/// that comes back.
///
/// Separated from the parts that need a browser and a socket so the decisions that MATTER can be
/// tested without either - above all the one that is a security control rather than a convenience:
/// a callback whose <c>state</c> does not match is refused.
/// </summary>
public static class AuthorizationCodeFlow
{
    /// <summary>
    /// The loopback host in the redirect URI.
    ///
    /// The IP literal, not <c>localhost</c>, per RFC 8252 §8.3: <c>localhost</c> depends on a name
    /// resolution the app does not control, and on a machine where it resolves to ::1 first the
    /// browser reaches a listener that is not there.
    /// </summary>
    public const string LoopbackHost = "127.0.0.1";

    /// <summary>
    /// Asks the OS for a free port rather than pinning one. See
    /// <see cref="RedirectUriFor"/> for why a user may want the opposite.
    /// </summary>
    public const int EphemeralPort = 0;

    /// <summary>
    /// The redirect URI a given port produces, without starting a sign-in.
    ///
    /// <para>Exists because the URI has to be REGISTERED with the provider before the first attempt,
    /// and deriving it from a failure is the worst way to learn it: the browser shows the provider's
    /// own error page and this app is never told anything at all. So the editor shows it up front -
    /// which it can only do for a pinned port, since an ephemeral one is not chosen until the moment
    /// the listener binds.</para>
    /// </summary>
    public static string RedirectUriFor(int port, string redirectPath = "/callback") =>
        port <= 0
            ? $"http://{LoopbackHost}:<a free port>{redirectPath}"
            : $"http://{LoopbackHost}:{port}{redirectPath}";

    /// <summary>
    /// Builds the authorize URL and the secrets that go with it.
    ///
    /// The redirect URI is a LOOPBACK address, per RFC 8252 §7.3 - the only redirect a desktop app can
    /// receive without registering a custom scheme or standing up a server, and the one providers
    /// expect from native clients. It has to be registered with the provider exactly as it appears
    /// here, which is why the caller is told what it is rather than it being an internal detail.
    /// </summary>
    public static AuthorizationRequest Build(
        string authorizeEndpoint,
        string clientId,
        string? scopes,
        int port,
        string redirectPath = "/callback",
        IEnumerable<KeyValuePair<string, string>>? extraParameters = null,
        string redirectHost = LoopbackHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizeEndpoint);

        var pkce = Pkce.Create();
        var state = Pkce.CreateState();
        var redirectUri = $"http://{redirectHost}:{port}{redirectPath}";

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["response_type"] = "code";
        query["client_id"] = clientId ?? "";
        query["redirect_uri"] = redirectUri;
        query["state"] = state;
        query["code_challenge"] = pkce.Challenge;
        query["code_challenge_method"] = pkce.Method;

        if (!string.IsNullOrWhiteSpace(scopes))
        {
            query["scope"] = scopes;
        }

        // Provider-specific extras, applied LAST so a provider that needs its own response_type or
        // prompt can say so - and applied through the same collection, so they are encoded rather than
        // pasted into the URL. The protocol parameters above are not special-cased into being
        // unoverridable: a user who genuinely needs a different one has no other way to say it, and
        // guessing which of somebody else's parameters are sacred is how this kind of allowlist gets
        // in the way of exactly the provider it was meant to support.
        foreach (var extra in extraParameters ?? [])
        {
            if (!string.IsNullOrWhiteSpace(extra.Key))
            {
                query[extra.Key.Trim()] = extra.Value ?? "";
            }
        }

        // An authorize endpoint may already carry query parameters - a tenant, an audience - and
        // throwing them away would break exactly the providers that need them.
        var separator = authorizeEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        return new AuthorizationRequest(
            authorizeEndpoint + separator + query.ToString(),
            redirectUri,
            state,
            pkce.Verifier);
    }

    /// <summary>
    /// Reads the redirect's query string, refusing anything whose state does not match.
    ///
    /// The state check is the security control, not a sanity check: without it, any request that
    /// reaches the loopback listener - a stray browser tab, a page that guessed the port - could hand
    /// this process a code and complete a sign-in nobody started. A mismatch is reported as an error
    /// rather than ignored, so it cannot look like the provider simply never answered.
    /// </summary>
    public static AuthorizationCallback ReadCallback(string? queryString, string expectedState)
    {
        var query = HttpUtility.ParseQueryString(Normalise(queryString));

        if (!string.Equals(query["state"], expectedState, StringComparison.Ordinal))
        {
            return new AuthorizationCallback(
                null,
                "state_mismatch",
                "The redirect did not carry the value this sign-in started with, so it was not answering this attempt.");
        }

        if (Value(query, "error") is { } error)
        {
            return new AuthorizationCallback(null, error, Value(query, "error_description"));
        }

        return Value(query, "code") is { } code
            ? new AuthorizationCallback(code, null, null)
            : new AuthorizationCallback(null, "no_code", "The redirect carried neither a code nor an error.");
    }

    private static string Normalise(string? queryString)
    {
        var text = queryString ?? "";

        return text.StartsWith('?') ? text[1..] : text;
    }

    private static string? Value(NameValueCollection query, string name) =>
        string.IsNullOrWhiteSpace(query[name]) ? null : query[name];
}
