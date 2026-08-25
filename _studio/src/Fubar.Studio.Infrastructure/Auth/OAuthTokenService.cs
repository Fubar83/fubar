using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Infrastructure.Auth;

/// <summary>
/// Talks to an OAuth 2.0 token endpoint for the no-browser grants. Client credentials sends
/// <c>grant_type=client_credentials</c> (+ scope); refresh token sends <c>grant_type=refresh_token</c>
/// with the stored refresh token. Client credentials go in the form body or an HTTP Basic header per
/// <see cref="OAuth2ClientAuth"/>. Parses the standard <c>access_token</c>/<c>expires_in</c>/
/// <c>refresh_token</c> response.
/// </summary>
public sealed class OAuthTokenService : IOAuthTokenService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OAuthTokenService(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    public async Task<OAuthTokenResult> AcquireAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.TokenUrl))
        {
            throw new InvalidOperationException("A token URL is required.");
        }

        if (request.Grant == OAuth2GrantType.RefreshToken && string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new InvalidOperationException("A refresh token is required for the refresh_token grant.");
        }

        var (form, basicCredentials) = BuildForm(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.TokenUrl);
        if (basicCredentials is not null)
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
        }

        httpRequest.Content = new FormUrlEncodedContent(form);

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token endpoint returned {(int)response.StatusCode}: {Truncate(body)}");
        }

        if (JsonNode.Parse(body) is not JsonObject json)
        {
            throw new InvalidOperationException("Token endpoint did not return a JSON object.");
        }

        var accessToken = json["access_token"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Token response had no access_token.");

        DateTimeOffset? expiresAt = null;
        if (json["expires_in"] is JsonValue expiresIn && TryGetSeconds(expiresIn, out var seconds))
        {
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        }

        var refreshToken = json["refresh_token"]?.GetValue<string>();
        return new OAuthTokenResult(accessToken, expiresAt, refreshToken);
    }

    // The form fields + (when using Basic-header auth) the base64 client credentials, shared by
    // AcquireAsync (to send) and Describe (to preview).
    private static (List<KeyValuePair<string, string>> Form, string? BasicCredentials) BuildForm(OAuth2TokenRequest request)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", request.Grant == OAuth2GrantType.RefreshToken ? "refresh_token" : "client_credentials"),
        };

        if (request.Grant == OAuth2GrantType.RefreshToken && !string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            form.Add(new("refresh_token", request.RefreshToken!));
        }

        if (!string.IsNullOrWhiteSpace(request.Scopes))
        {
            form.Add(new("scope", request.Scopes!));
        }

        string? basicCredentials = null;
        if (request.ClientAuthentication == OAuth2ClientAuth.BasicHeader && !string.IsNullOrEmpty(request.ClientId))
        {
            basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{request.ClientId}:{request.ClientSecret}"));
        }
        else
        {
            if (!string.IsNullOrEmpty(request.ClientId))
            {
                form.Add(new("client_id", request.ClientId!));
            }

            if (!string.IsNullOrEmpty(request.ClientSecret))
            {
                form.Add(new("client_secret", request.ClientSecret!));
            }
        }

        return (form, basicCredentials);
    }

    public string Describe(OAuth2TokenRequest request)
    {
        var (form, basicCredentials) = BuildForm(request);
        var builder = new StringBuilder();
        builder.AppendLine($"POST {request.TokenUrl}");
        builder.AppendLine("Content-Type: application/x-www-form-urlencoded");
        if (basicCredentials is not null)
        {
            builder.AppendLine("Authorization: Basic ••••••••   (client id/secret)");
        }

        builder.AppendLine();
        foreach (var (key, value) in form)
        {
            builder.AppendLine($"{key}={MaskSecret(key, value)}");
        }

        return builder.ToString().TrimEnd();
    }

    // Show config values so the user can verify them, but never echo secrets.
    private static string MaskSecret(string key, string value) =>
        key is "client_secret" or "refresh_token" ? "••••••••" : value;

    private static bool TryGetSeconds(JsonValue value, out long seconds)
    {
        if (value.TryGetValue(out seconds))
        {
            return true;
        }

        return value.TryGetValue<string>(out var text) && long.TryParse(text, out seconds);
    }

    private static string Truncate(string body) => body.Length <= 300 ? body : body[..300] + "…";
}
