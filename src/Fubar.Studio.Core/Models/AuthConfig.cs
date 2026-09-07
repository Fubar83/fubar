namespace Fubar.Studio.Core.Models;

/// <summary>Auth scheme selector, matching the request builder's Auth tab inheritance selector
/// (RequestEditorPane.md §5: "Select an existing Auth Profile ... inherit from the parent folder,
/// or explicitly disable authentication").</summary>
public enum AuthType
{
    /// <summary>Use whatever the nearest ancestor folder (or the workspace default) resolves to.</summary>
    Inherit,

    /// <summary>Explicitly send no auth, overriding anything that would otherwise be inherited.</summary>
    None,

    Bearer,
    ApiKey,
    Basic,
    OAuth2,

    /// <summary>Use a named, reusable <see cref="AuthProfile"/> - see <see cref="RequestModel.AuthProfileId"/>
    /// / <see cref="FolderConfig.AuthProfileId"/>.</summary>
    Profile,
}

public enum ApiKeyLocation
{
    Header,
    QueryParam,
}

/// <summary>Supported OAuth 2.0 grant types (no-browser flows).</summary>
public enum OAuth2GrantType
{
    /// <summary>Machine-to-machine: exchange client id/secret (+ scopes) for a token.</summary>
    ClientCredentials,

    /// <summary>Exchange a stored refresh token for a fresh access token.</summary>
    RefreshToken,

    /// <summary>
    /// Sign in as a PERSON: the browser is opened at the provider, they approve, and the code that
    /// comes back is exchanged for a token. Always with PKCE - see <see cref="Auth.Pkce"/>.
    ///
    /// The only grant here that needs a user present, which is why it needs a browser and a loopback
    /// listener rather than just a request.
    /// </summary>
    AuthorizationCode,
}

/// <summary>How the client credentials are presented to the token endpoint.</summary>
public enum OAuth2ClientAuth
{
    /// <summary>client_id/client_secret in the form body.</summary>
    Body,

    /// <summary>HTTP Basic <c>Authorization</c> header (base64 of client_id:client_secret).</summary>
    BasicHeader,
}

/// <summary>
/// Auth parameters for either the workspace default (<c>auth.json</c>) or a single request's
/// <c>auth</c> field. Only the fields relevant to <see cref="Type"/> are populated; the rest stay
/// null. Applying this to an outgoing request is an <c>IAuthProvider</c>'s job (Phase 5) - this
/// type is pure data.
/// </summary>
public sealed class AuthConfig
{
    public AuthType Type { get; set; } = AuthType.Inherit;

    // Bearer
    public string? Token { get; set; }

    // ApiKey
    public string? ApiKeyName { get; set; }
    public string? ApiKeyValue { get; set; }
    public ApiKeyLocation ApiKeyLocation { get; set; } = ApiKeyLocation.Header;

    // Basic
    public string? Username { get; set; }
    public string? Password { get; set; }

    // OAuth2. Any string field may be a literal or contain {{variables}} (resolved at send/test time),
    // so values can be hardcoded, pulled from variables, or inherited via a profile/folder.
    public string? AccessToken { get; set; }
    public OAuth2GrantType OAuth2Grant { get; set; } = OAuth2GrantType.ClientCredentials;
    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scopes { get; set; }
    public string? RefreshToken { get; set; }
    public OAuth2ClientAuth ClientAuthentication { get; set; } = OAuth2ClientAuth.Body;

    /// <summary>Name of the (session-only, non-persisted) variable the acquired access token is stashed
    /// in and read from for the <c>Authorization: Bearer</c> header. Defaults to
    /// <c>oauth2_access_token</c> when null.</summary>
    public string? AccessTokenVariable { get; set; }

    /// <summary>Name of the session variable holding the token's expiry (unix seconds). Defaults to
    /// <c>oauth2_expires_at</c>.</summary>
    public string? ExpiryVariable { get; set; }

    // Template-based OAuth2 (the request-builder-style editor). When these are present the auth provider
    // runs the editable token request + captures instead of the legacy fixed-form path. All additive and
    // defaulted so existing configs (TokenRequest == null) keep routing to the legacy engine.

    /// <summary>The editable token/login request (method/URL/headers/body), seeded from an
    /// <c>AuthTemplate</c>. <c>null</c> ⇒ use the legacy fixed-form OAuth2 path built by the token service.</summary>
    public AuthTokenRequest? TokenRequest { get; set; }

    /// <summary>JSONPath → variable rules applied to the token response on success (and cleared on
    /// failure). The provider forces these to <see cref="CaptureScope.Session"/>; the rule writing
    /// <see cref="AccessTokenVariable"/> is the token the <c>Authorization: Bearer</c> header references.</summary>
    public List<CaptureRule> TokenCaptures { get; set; } = [];

    /// <summary>JSONPath to the relative <c>expires_in</c> seconds in the token response, used only to
    /// compute <see cref="ExpiryVariable"/> for caching. Kept out of the user captures grid.</summary>
    public string? ExpiresInExpression { get; set; }

    // Signing a PERSON in (authorization code). The browser half is not a request, so none of it fits
    // in TokenRequest - and until these existed none of it was saved at all: a profile reopened with
    // an empty authorize URL, so every session began by rediscovering the provider before the sign-in
    // button could do anything.

    /// <summary>
    /// Where to send the browser for an authorization code. Filled by Discover or by choosing a
    /// provider; <c>null</c> for every grant that needs no browser.
    /// </summary>
    public string? AuthorizeUrl { get; set; }

    /// <summary>
    /// Extra query parameters for <see cref="AuthorizeUrl"/> - Google's <c>access_type=offline</c>,
    /// Auth0's <c>audience</c>. Provider requirements rather than user preferences, which is why a
    /// preset seeds them; still editable, because the next provider will want something else.
    /// </summary>
    public List<KeyValueItem> AuthorizeParameters { get; set; } = [];

    /// <summary>
    /// The loopback port to catch the redirect on. <c>null</c> or 0 asks the OS for a free one.
    ///
    /// <para>Worth pinning, and worth persisting once pinned: the redirect URI carries the port, and
    /// most providers match it exactly, so a port that changes per attempt makes the URI impossible to
    /// register. Google and Entra are the exceptions that ignore it - which is precisely why the
    /// default cannot simply be "always ephemeral".</para>
    /// </summary>
    public int? RedirectPort { get; set; }

    /// <summary>
    /// Which <c>SignInProvider</c> this was set up for, so a saved profile reopens on it. Purely a UI
    /// affordance - nothing about sending depends on it, and an unknown key is ignored rather than
    /// rejected, so a profile written by a newer version still loads.
    /// </summary>
    public string? SignInProviderKey { get; set; }

    /// <summary>
    /// The tenant, directory or domain a provider's URLs were built for - Entra's tenant id, an Okta
    /// domain. Kept beside the key so reopening a profile shows what it was set up against, rather
    /// than a generic URL the user has to reverse-engineer.
    /// </summary>
    public string? SignInTenant { get; set; }
}
