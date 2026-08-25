using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>A token endpoint response: the access token, when it expires (if the server said), and a
/// refresh token (if issued).</summary>
public sealed record OAuthTokenResult(string AccessToken, DateTimeOffset? ExpiresAt, string? RefreshToken);

/// <summary>A fully-resolved (no <c>{{variables}}</c> left) request to a token endpoint.</summary>
public sealed record OAuth2TokenRequest(
    OAuth2GrantType Grant,
    string TokenUrl,
    string? ClientId,
    string? ClientSecret,
    string? Scopes,
    string? RefreshToken,
    OAuth2ClientAuth ClientAuthentication);

/// <summary>Performs the OAuth 2.0 token request for the supported no-browser grants (client
/// credentials, refresh token) and parses the standard token response.</summary>
public interface IOAuthTokenService
{
    Task<OAuthTokenResult> AcquireAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>A human-readable preview of the exact HTTP token request (method, URL, headers, form
    /// body) that <see cref="AcquireAsync"/> would send, with secrets masked - for a "verify request"
    /// button. Makes no network call.</summary>
    string Describe(OAuth2TokenRequest request);
}
