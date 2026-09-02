using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// The built-in auth templates offered by the request-builder-style editor. The OAuth2 seeds reproduce
/// what the legacy fixed-form path emits (form-encoded body with <c>grant_type</c>/<c>client_id</c>/
/// <c>scope</c>/…), so a freshly-seeded template behaves like today's OAuth2 form while remaining fully
/// editable. Client credentials go in the form body (variable-safe); a server that strictly requires HTTP
/// Basic can have an <c>Authorization</c> header added by hand.
/// </summary>
public static class AuthTemplateCatalog
{
    public static IReadOnlyList<AuthTemplate> All { get; } =
    [
        ClientCredentials(),
        AuthorizationCode(),
        RefreshToken(),
        CustomLogin(),
    ];

    /// <summary>The template seeded for a brand-new OAuth2 profile.</summary>
    public static AuthTemplate Default => All[0];

    private static AuthTemplate ClientCredentials() => new(
        Key: "oauth2-client-credentials",
        DisplayName: "OAuth 2.0 - Client Credentials",
        Grant: OAuth2GrantType.ClientCredentials,
        SeedRequest: new AuthTokenRequest
        {
            Method = "POST",
            Url = "{{token_url}}",
            Body = UrlEncoded(
                ("grant_type", "client_credentials"),
                ("scope", "{{scopes}}"),
                ("client_id", "{{client_id}}"),
                ("client_secret", "{{client_secret}}")),
        },
        SeedCaptures: [Capture(AuthDefaults.AccessTokenVariable, "$.access_token")],
        AccessTokenVariable: AuthDefaults.AccessTokenVariable,
        ExpiryVariable: AuthDefaults.ExpiryVariable,
        ExpiresInExpression: "$.expires_in");

    /// <summary>
    /// Sign in as a person. The token request here is only the SECOND half of the grant - the exchange
    /// of an authorization code for a token - because the first half is a browser round trip that no
    /// request can express.
    ///
    /// <c>{{oauth2_code}}</c> and <c>{{oauth2_code_verifier}}</c> are written into the session by the
    /// browser step immediately before this request runs, which is why they appear here as ordinary
    /// variables: the exchange stays an editable request like every other, and a provider wanting an
    /// extra field can have one added by hand.
    /// </summary>
    private static AuthTemplate AuthorizationCode() => new(
        Key: "oauth2-authorization-code",
        DisplayName: "OAuth 2.0 - Authorization Code + PKCE (sign in)",
        Grant: OAuth2GrantType.AuthorizationCode,
        SeedRequest: new AuthTokenRequest
        {
            Method = "POST",
            Url = "{{token_url}}",
            Body = UrlEncoded(
                ("grant_type", "authorization_code"),
                ("code", "{{oauth2_code}}"),
                ("redirect_uri", "{{oauth2_redirect_uri}}"),
                ("code_verifier", "{{oauth2_code_verifier}}"),
                ("client_id", "{{client_id}}")),
        },
        SeedCaptures:
        [
            Capture(AuthDefaults.AccessTokenVariable, "$.access_token"),
            Capture("oauth2_refresh_token", "$.refresh_token"),
        ],
        AccessTokenVariable: AuthDefaults.AccessTokenVariable,
        ExpiryVariable: AuthDefaults.ExpiryVariable,
        ExpiresInExpression: "$.expires_in");

    private static AuthTemplate RefreshToken() => new(
        Key: "oauth2-refresh-token",
        DisplayName: "OAuth 2.0 - Refresh Token",
        Grant: OAuth2GrantType.RefreshToken,
        SeedRequest: new AuthTokenRequest
        {
            Method = "POST",
            Url = "{{token_url}}",
            Body = UrlEncoded(
                ("grant_type", "refresh_token"),
                ("refresh_token", "{{oauth2_refresh_token}}"),
                ("scope", "{{scopes}}"),
                ("client_id", "{{client_id}}"),
                ("client_secret", "{{client_secret}}")),
        },
        // Rotate the refresh token in place: the capture writes the same variable the body reads.
        SeedCaptures:
        [
            Capture(AuthDefaults.AccessTokenVariable, "$.access_token"),
            Capture("oauth2_refresh_token", "$.refresh_token"),
        ],
        AccessTokenVariable: AuthDefaults.AccessTokenVariable,
        ExpiryVariable: AuthDefaults.ExpiryVariable,
        ExpiresInExpression: "$.expires_in");

    private static AuthTemplate CustomLogin() => new(
        Key: "custom-login",
        DisplayName: "Custom login request",
        Grant: null,
        SeedRequest: new AuthTokenRequest
        {
            Method = "POST",
            Url = "",
            Body = new RequestBody
            {
                Type = BodyType.Json,
                Raw = "{\n  \"username\": \"{{username}}\",\n  \"password\": \"{{password}}\"\n}",
            },
        },
        SeedCaptures: [Capture("token", "$.token")],
        AccessTokenVariable: "token",
        ExpiryVariable: "token_expires_at",
        ExpiresInExpression: null);

    private static RequestBody UrlEncoded(params (string Key, string Value)[] fields) => new()
    {
        Type = BodyType.UrlEncoded,
        UrlEncoded = [.. fields.Select(f => new KeyValueItem { Key = f.Key, Value = f.Value })],
    };

    private static CaptureRule Capture(string variableName, string expression) => new()
    {
        VariableName = variableName,
        Source = ResponseField.JsonBody,
        Expression = expression,
        Scope = CaptureScope.Session,
    };
}
