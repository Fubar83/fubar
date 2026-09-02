using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// The parts of an OpenID Connect discovery document this app can act on.
/// </summary>
/// <param name="Issuer">The issuer, for showing the user which provider answered.</param>
/// <param name="TokenEndpoint">Where to POST for a token.</param>
/// <param name="AuthorizationEndpoint">Where to send a browser for an authorization code, if offered.</param>
/// <param name="ScopesSupported">The scopes the provider advertises, so they can be offered rather than typed from memory.</param>
/// <param name="GrantTypesSupported">The grants it advertises, so an unsupported one can be said out loud.</param>
public sealed record OpenIdConfiguration(
    string? Issuer,
    string? TokenEndpoint,
    string? AuthorizationEndpoint,
    IReadOnlyList<string> ScopesSupported,
    IReadOnlyList<string> GrantTypesSupported);

/// <summary>
/// Reads a provider's <c>/.well-known/openid-configuration</c>.
///
/// Setting OAuth up starts with copying a token endpoint out of a provider's documentation, and every
/// OIDC provider publishes that endpoint - along with its scopes and supported grants - at a
/// predictable URL. Fetching it turns "find the docs, find the right page, copy the URL, hope it is
/// the current one" into pasting the issuer.
///
/// Parsing is separate from fetching, and pure, so the awkward parts - a provider that returns HTML,
/// one that omits half the fields - are testable without a network.
/// </summary>
public static class OpenIdDiscovery
{
    /// <summary>
    /// Turns whatever the user pasted into the URL to fetch.
    ///
    /// People paste three things: the issuer, the well-known URL itself, or the issuer with a trailing
    /// slash. All three should work, because being told "that is not a discovery URL" when you pasted
    /// the thing your provider's page calls the issuer is not help.
    /// </summary>
    public static string? WellKnownUrlFor(string? issuerOrUrl)
    {
        if (string.IsNullOrWhiteSpace(issuerOrUrl))
        {
            return null;
        }

        var text = issuerOrUrl.Trim().TrimEnd('/');

        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text;
        }

        return text.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase)
            ? text
            : text + "/.well-known/openid-configuration";
    }

    /// <summary>
    /// Parses a discovery document, or returns null when the body is not one.
    ///
    /// Null rather than an exception: the commonest failures are a typo'd issuer answering with an
    /// HTML 404 page and a provider that is simply not OIDC, and neither is exceptional.
    /// </summary>
    public static OpenIdConfiguration? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject obj)
        {
            return null;
        }

        var tokenEndpoint = Text(obj, "token_endpoint");

        // A document with no token endpoint cannot help with the one thing this is for, and treating
        // it as a success would fill the editor with nothing and say it worked.
        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return null;
        }

        return new OpenIdConfiguration(
            Text(obj, "issuer"),
            tokenEndpoint,
            Text(obj, "authorization_endpoint"),
            Strings(obj, "scopes_supported"),
            Strings(obj, "grant_types_supported"));
    }

    private static string? Text(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) && node is JsonValue value ? value.ToString() : null;

    private static IReadOnlyList<string> Strings(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) && node is JsonArray array
            ? [.. array.OfType<JsonValue>().Select(v => v.ToString()).Where(s => !string.IsNullOrWhiteSpace(s))]
            : [];
}
