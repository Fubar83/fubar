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
    public static (AuthTokenRequest Request, List<CaptureRule> Captures) FromLegacy(AuthConfig legacy)
    {
        var accessTokenVariable = string.IsNullOrWhiteSpace(legacy.AccessTokenVariable)
            ? AuthDefaults.AccessTokenVariable
            : legacy.AccessTokenVariable!;

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

        if (!string.IsNullOrWhiteSpace(legacy.ClientId))
        {
            fields.Add(new KeyValueItem { Key = "client_id", Value = legacy.ClientId! });
        }

        if (!string.IsNullOrWhiteSpace(legacy.ClientSecret))
        {
            fields.Add(new KeyValueItem { Key = "client_secret", Value = legacy.ClientSecret! });
        }

        var request = new AuthTokenRequest
        {
            Method = "POST",
            Url = legacy.TokenUrl ?? "",
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
