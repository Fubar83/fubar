using System.Text;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>The credential material a scheme contributes to an outgoing request: headers and/or query
/// params. Injected into the request by the execution pipeline (<c>AuthRequestMerge</c>) just before send.</summary>
public sealed record AppliedAuth(IReadOnlyList<KeyValueItem> Headers, IReadOnlyList<KeyValueItem> QueryParams)
{
    public static AppliedAuth Empty { get; } = new([], []);

    public bool IsEmpty => Headers.Count == 0 && QueryParams.Count == 0;
}

/// <summary>An <see cref="AuthConfig"/> with its <c>{{variables}}</c> already resolved (and, for OAuth2,
/// the acquired access token filled in). The pure <see cref="AuthApplier"/> turns this into headers/query.</summary>
public readonly record struct ResolvedAuth(
    AuthType Type,
    string? Token,
    string? AccessToken,
    string? ApiKeyName,
    string? ApiKeyValue,
    ApiKeyLocation ApiKeyLocation,
    string? Username,
    string? Password);

/// <summary>
/// The single domain rule for how a scheme applies to an outgoing request. <see cref="Build"/> produces the
/// real, resolved credential material for injection at send time (Bearer/OAuth2 bearer header, API key in a
/// header or query param, HTTP Basic with base64 computed <b>after</b> variable resolution).
/// <see cref="BuildPreview"/> produces masked, illustrative placeholder rows for the request view so the
/// user can see auth will be sent, without leaking secrets. Supersedes <c>AuthHeaderResolver</c>.
/// </summary>
public static class AuthApplier
{
    private const string Mask = "••••••••";

    public static AppliedAuth Build(ResolvedAuth r)
    {
        switch (r.Type)
        {
            case AuthType.Bearer:
                return Header("Authorization", $"Bearer {r.Token}");

            case AuthType.OAuth2:
                return string.IsNullOrEmpty(r.AccessToken)
                    ? AppliedAuth.Empty // no token acquired - the outcome reports why; don't send "Bearer "
                    : Header("Authorization", $"Bearer {r.AccessToken}");

            case AuthType.Basic:
                return string.IsNullOrEmpty(r.Username) && string.IsNullOrEmpty(r.Password)
                    ? AppliedAuth.Empty
                    : Header("Authorization", $"Basic {Base64($"{r.Username}:{r.Password}")}");

            case AuthType.ApiKey when !string.IsNullOrWhiteSpace(r.ApiKeyName):
                var item = new KeyValueItem { Key = r.ApiKeyName!, Value = r.ApiKeyValue ?? "" };
                return r.ApiKeyLocation == ApiKeyLocation.QueryParam
                    ? new AppliedAuth([], [item])
                    : new AppliedAuth([item], []);

            default:
                return AppliedAuth.Empty;
        }
    }

    /// <summary>Masked, illustrative placeholder rows for the request view (secrets shown as bullets,
    /// OAuth2 shown as its <c>{{token}}</c> reference).</summary>
    public static AppliedAuth BuildPreview(AuthConfig config)
    {
        switch (config.Type)
        {
            case AuthType.Bearer:
                return Header("Authorization", $"Bearer {Placeholder(config.Token)}");

            case AuthType.OAuth2:
                return Header("Authorization", $"Bearer {{{{{OAuthTokenVariable(config)}}}}}");

            case AuthType.Basic:
                return Header("Authorization", $"Basic {Mask}");

            case AuthType.ApiKey when !string.IsNullOrWhiteSpace(config.ApiKeyName):
                var item = new KeyValueItem { Key = config.ApiKeyName!, Value = Placeholder(config.ApiKeyValue) };
                return config.ApiKeyLocation == ApiKeyLocation.QueryParam
                    ? new AppliedAuth([], [item])
                    : new AppliedAuth([item], []);

            default:
                return AppliedAuth.Empty;
        }
    }

    private static AppliedAuth Header(string key, string value) =>
        new([new KeyValueItem { Key = key, Value = value }], []);

    private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    // Show a variable reference as-is (it isn't a secret), otherwise mask a literal value.
    private static string Placeholder(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value.Contains("{{", StringComparison.Ordinal) ? value : Mask;

    private static string OAuthTokenVariable(AuthConfig config) =>
        string.IsNullOrWhiteSpace(config.AccessTokenVariable) ? AuthDefaults.AccessTokenVariable : config.AccessTokenVariable!;
}
