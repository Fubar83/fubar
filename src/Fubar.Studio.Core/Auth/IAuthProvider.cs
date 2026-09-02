using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>The result of ensuring/testing auth: whether it succeeded and a human-readable message
/// (e.g. "Token acquired, expires 2026-08-17 12:00Z" or the token-endpoint error).</summary>
public sealed record AuthOutcome(bool Ok, string Message)
{
    /// <summary>
    /// What the token endpoint actually replied, when one was called.
    ///
    /// Carried on the outcome because a capture rule is a JSONPath into THIS, and until it was shown
    /// the one step needing exact knowledge of the payload was the one step with no way to see it.
    /// Present on failure too - especially on failure, since that is when the body says
    /// <c>invalid_client</c> and what was wrong with it.
    /// </summary>
    public TokenResponse? Response { get; init; }
}

/// <summary>
/// The token endpoint's reply, kept only long enough to show it.
/// </summary>
/// <param name="StatusCode">The HTTP status, so a 200 with a useless body is distinguishable from a 401.</param>
/// <param name="Body">The raw body, as received.</param>
public sealed record TokenResponse(int StatusCode, string Body)
{
    /// <summary>The paths a capture rule could use - see <see cref="TokenResponseFields"/>.</summary>
    public IReadOnlyList<TokenResponseField> Fields => TokenResponseFields.From(Body);
}

/// <summary>The prestep's output: the credential material to inject into the outgoing request, plus a
/// human-readable outcome (for status logging / the Test button).</summary>
public sealed record AuthPreparation(AppliedAuth Applied, AuthOutcome Outcome);

/// <summary>
/// The auth <b>prestep</b> for a request: acquire (for OAuth 2.0 / custom token requests, mint or reuse a
/// per-(workspace,environment) session token, running the token request + captures) then apply (produce
/// the resolved headers/query params the pipeline injects into the request). Static schemes
/// (Bearer/API key/Basic) skip acquire and just apply. Also drives the Auth tab's "Test" button.
/// </summary>
public interface IAuthProvider
{
    /// <param name="forceReacquire">Bypass the cached-token short-circuit and re-acquire (used for the
    /// retry-once-on-401 path).</param>
    Task<AuthPreparation> PrepareAsync(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment, bool forceReacquire = false, CancellationToken cancellationToken = default);

    /// <summary>The Apply half only (no acquire): resolves the scheme's variables and reads any
    /// already-acquired token from the session to produce the credential material - used for a faithful
    /// "Copy as cURL" without triggering a token request.</summary>
    AppliedAuth Apply(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment);

    /// <summary>Resolves the auth's variables and returns a preview of the OAuth token request that would
    /// be sent (secrets masked, no network call), for a "Verify request" button - so the user can confirm
    /// the endpoint/body/credentials are right before testing.</summary>
    string PreviewTokenRequest(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment);
}
