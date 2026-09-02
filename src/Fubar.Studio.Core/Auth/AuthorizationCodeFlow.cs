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
        string redirectPath = "/callback")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizeEndpoint);

        var pkce = Pkce.Create();
        var state = Pkce.CreateState();
        var redirectUri = $"http://127.0.0.1:{port}{redirectPath}";

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
