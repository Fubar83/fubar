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
}
