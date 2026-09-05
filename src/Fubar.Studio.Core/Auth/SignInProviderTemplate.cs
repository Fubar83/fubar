using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// Turns a <see cref="SignInProvider"/> into a ready-to-run authorization-code template.
///
/// <para>Separate from the catalog because the catalog is a table of facts and this is the one
/// decision in the feature: given those facts, what should the editor be filled with. Pure, so the
/// awkward cases - a provider that needs a client secret, one with no discovery document, a tenant
/// left empty - are testable without a browser, a socket or a UI.</para>
/// </summary>
public static class SignInProviderTemplate
{
    /// <summary>
    /// The seed for signing in with <paramref name="provider"/>.
    ///
    /// <para>The token request is the SECOND half of the grant - exchanging the code - because the
    /// first half is a browser round trip no request can express. <c>{{oauth2_code}}</c> and
    /// <c>{{oauth2_code_verifier}}</c> are written into the session by that browser step immediately
    /// before this request runs, which is why they appear as ordinary variables.</para>
    ///
    /// <para>The client id and secret are <c>{{variables}}</c> rather than blank boxes for the reason
    /// the whole codebase repeats: a profile is committed, and a literal secret in one is a secret in
    /// everybody's checkout. The editor's variable summary then says which are undefined, before
    /// anything is sent.</para>
    /// </summary>
    public static AuthTemplate For(SignInProvider provider, string? tenant = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var fields = new List<KeyValueItem>
        {
            Field("grant_type", "authorization_code"),
            Field("code", "{{oauth2_code}}"),
            Field("redirect_uri", "{{oauth2_redirect_uri}}"),
            Field("code_verifier", "{{oauth2_code_verifier}}"),
            Field("client_id", "{{client_id}}"),
        };

        if (provider.NeedsClientSecret)
        {
            fields.Add(Field("client_secret", "{{client_secret}}"));
        }

        return new AuthTemplate(
            Key: $"signin-{provider.Key}",
            DisplayName: $"Sign in with {provider.DisplayName}",
            Grant: OAuth2GrantType.AuthorizationCode,
            SeedRequest: new AuthTokenRequest
            {
                Method = "POST",
                Url = provider.TokenEndpointFor(tenant) ?? "{{token_url}}",

                // Asked for explicitly because one provider in the catalog answers with form-encoded
                // data otherwise, and a form-encoded body defeats the JSONPath captures below - the
                // token arrives, nothing captures it, and the editor reports a 200 with no token.
                Headers = [new KeyValueItem { Key = "Accept", Value = "application/json" }],
                Body = new RequestBody
                {
                    Type = BodyType.UrlEncoded,
                    UrlEncoded = fields,
                },
            },

            // id_token is captured too, and deliberately: this grant signs a PERSON in, and the id
            // token is the part that says who. Nothing sends it anywhere - it is there to be looked at
            // and asserted on, which is most of what an API client is for.
            SeedCaptures:
            [
                Capture(AuthDefaults.AccessTokenVariable, "$.access_token"),
                Capture("oauth2_refresh_token", "$.refresh_token"),
                Capture("oauth2_id_token", "$.id_token"),
            ],
            AccessTokenVariable: AuthDefaults.AccessTokenVariable,
            ExpiryVariable: AuthDefaults.ExpiryVariable,
            ExpiresInExpression: "$.expires_in",
            AuthorizeUrl: provider.AuthorizeEndpointFor(tenant) ?? "",
            AuthorizeParameters: [.. provider.AuthorizeParameters.Select(Copy)],
            ProviderKey: provider.Key);
    }

    /// <summary>
    /// The scopes this provider's sign-in starts with, as the token request carries them: one
    /// space-separated field, which is what OAuth 2.0 says a scope list is.
    /// </summary>
    public static string ScopeValue(SignInProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return string.Join(' ', provider.DefaultScopes);
    }

    private static KeyValueItem Field(string key, string value) => new() { Key = key, Value = value };

    // Copied rather than shared: the catalog's lists are static, the editor's rows are edited, and a
    // preset whose defaults drifted because someone typed in a box would be a genuinely baffling bug.
    private static KeyValueItem Copy(KeyValueItem item) =>
        new() { Enabled = item.Enabled, Key = item.Key, Value = item.Value };

    private static CaptureRule Capture(string variableName, string expression) => new()
    {
        VariableName = variableName,
        Source = ResponseField.JsonBody,
        Expression = expression,
        Scope = CaptureScope.Session,
    };
}
