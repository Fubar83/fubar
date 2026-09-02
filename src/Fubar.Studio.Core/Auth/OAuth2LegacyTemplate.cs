using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// Upgrades a legacy fixed-form OAuth2 <see cref="AuthConfig"/> (no <see cref="AuthConfig.TokenRequest"/>)
/// into the editable request + captures the new template editor uses, mirroring what
/// <c>OAuthTokenService.BuildForm</c> sends. The editor calls this when opening an old profile so the
/// request-builder view is pre-filled with the profile's actual values; the upgrade persists on save.
/// Client credentials are mapped into the form body (variable-safe, resolved at send time) regardless of
/// the legacy <see cref="OAuth2ClientAuth"/> setting - a server that strictly requires an HTTP Basic
/// header can have one added by hand after the upgrade.
/// </summary>
public static class OAuth2LegacyTemplate
{
    /// <summary>Maps the OAuth2 fields of <paramref name="legacy"/> to a token request + capture rules.
    /// The access-token capture uses the config's effective access-token variable so the
    /// <c>Authorization: Bearer</c> header keeps resolving to the same name.</summary>
    /// <param name="resolveCredentials">
    /// Optional. When supplied AND the legacy config asked for HTTP Basic client authentication, the
    /// credentials are resolved through this and emitted as an <c>Authorization: Basic</c> header
    /// instead of body fields - which is the only way to preserve that setting, because the header
    /// value is base64 of <c>id:secret</c> and cannot be built while those are still <c>{{tokens}}</c>.
    ///
    /// The EDITOR passes null: it is showing the user an editable request, and baking a resolved
    /// secret into a header it is about to save to disk would be exactly wrong. The PROVIDER passes
    /// one, because by then it has the workspace and environment and the result is never persisted.
    /// </param>
    public static (AuthTokenRequest Request, List<CaptureRule> Captures) FromLegacy(
        AuthConfig legacy,
        Func<string?, string?>? resolveCredentials = null)
    {
        var accessTokenVariable = string.IsNullOrWhiteSpace(legacy.AccessTokenVariable)
            ? AuthDefaults.AccessTokenVariable
            : legacy.AccessTokenVariable!;

        // Basic only when we can actually build it. Without a resolver the credentials may still be
        // variables, and a header of "Basic {{clientId}}:{{secret}}" base64'd is worse than useless -
        // so the body form is used, which resolves correctly at send time.
        var useBasicHeader = legacy.ClientAuthentication == OAuth2ClientAuth.BasicHeader
            && resolveCredentials is not null
            && !string.IsNullOrWhiteSpace(legacy.ClientId);

        var fields = new List<KeyValueItem>
        {
            new()
            {
                Key = "grant_type",
                Value = legacy.OAuth2Grant == OAuth2GrantType.RefreshToken ? "refresh_token" : "client_credentials",
            },
        };

        if (legacy.OAuth2Grant == OAuth2GrantType.RefreshToken && !string.IsNullOrWhiteSpace(legacy.RefreshToken))
        {
            fields.Add(new KeyValueItem { Key = "refresh_token", Value = legacy.RefreshToken! });
        }

        if (!string.IsNullOrWhiteSpace(legacy.Scopes))
        {
            fields.Add(new KeyValueItem { Key = "scope", Value = legacy.Scopes! });
        }

        if (!useBasicHeader && !string.IsNullOrWhiteSpace(legacy.ClientId))
        {
            fields.Add(new KeyValueItem { Key = "client_id", Value = legacy.ClientId! });
        }

        if (!useBasicHeader && !string.IsNullOrWhiteSpace(legacy.ClientSecret))
        {
            fields.Add(new KeyValueItem { Key = "client_secret", Value = legacy.ClientSecret! });
        }

        var headers = new List<KeyValueItem>();

        if (useBasicHeader)
        {
            var id = resolveCredentials!(legacy.ClientId) ?? string.Empty;
            var secret = resolveCredentials!(legacy.ClientSecret) ?? string.Empty;

            headers.Add(new KeyValueItem
            {
                Key = "Authorization",
                Value = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{id}:{secret}")),
            });
        }

        var request = new AuthTokenRequest
        {
            Method = "POST",
            Url = legacy.TokenUrl ?? "",
            Headers = headers,
            Body = new RequestBody { Type = BodyType.UrlEncoded, UrlEncoded = fields },
        };

        var captures = new List<CaptureRule>
        {
            new()
            {
                VariableName = accessTokenVariable,
                Source = ResponseField.JsonBody,
                Expression = "$.access_token",
                Scope = CaptureScope.Session,
            },
        };

        return (request, captures);
    }
}
