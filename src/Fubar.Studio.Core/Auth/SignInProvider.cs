using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// A named identity provider you can sign in to, with everything that is TRUE OF THAT PROVIDER rather
/// than of the person using it.
///
/// <para>The authorization-code template already worked for any provider - if you knew the authorize
/// endpoint, the token endpoint, the scope syntax, which extra query parameters that provider needs
/// before it will part with a refresh token, and whether it wants a client secret from a desktop app.
/// That is five pieces of provider trivia standing between someone and a sign-in, and every one of
/// them fails the same way: the browser shows the provider's error page and the app hears nothing.
/// These records hold the trivia so the user supplies only what is genuinely theirs - a client id, a
/// tenant, a secret.</para>
///
/// <para>Data, not behaviour. Everything here could be typed into the editor by hand, and everything
/// here stays editable afterwards - a preset that locked its fields would be a worse version of the
/// fixed-form OAuth screen this editor replaced.</para>
/// </summary>
/// <param name="Key">Stable identifier, persisted so a saved profile reopens on the right provider.</param>
/// <param name="DisplayName">What the picker shows.</param>
/// <param name="IssuerTemplate">
/// The OIDC issuer, with <c>{tenant}</c> where one is needed. Null for a provider that publishes no
/// discovery document (GitHub), and for Custom, where the user pastes their own.
/// </param>
/// <param name="AuthorizeEndpoint">Authorize endpoint, when it is not worth a discovery round trip to learn.</param>
/// <param name="TokenEndpoint">Token endpoint, likewise.</param>
/// <param name="DefaultScopes">Scopes that get a usable sign-in, space-joined into the request.</param>
/// <param name="AuthorizeParameters">
/// Extra query parameters the AUTHORIZE URL needs. This is where the folklore lives: Google returns a
/// refresh token only with <c>access_type=offline</c>, and only re-issues one with
/// <c>prompt=consent</c>. Without them a sign-in appears to work and then silently cannot be renewed.
/// </param>
/// <param name="NeedsClientSecret">
/// Whether the token exchange must carry a client secret. True for Google and GitHub, which issue one
/// even to desktop clients; false for Microsoft, where a public client must NOT send one.
/// </param>
/// <param name="TenantLabel">What the provider calls its tenant field, or null when it has none.</param>
/// <param name="TenantDefault">A tenant value that works out of the box, where one exists.</param>
/// <param name="TenantHelp">One sentence saying what to put in the tenant field.</param>
/// <param name="SetupSummary">What to do in the provider's own console before any of this works.</param>
/// <param name="ConsoleUrl">Where that console is.</param>
/// <param name="Caveat">Anything true of this provider that the user would otherwise discover the hard way.</param>
public sealed record SignInProvider(
    string Key,
    string DisplayName,
    string? IssuerTemplate,
    string? AuthorizeEndpoint,
    string? TokenEndpoint,
    IReadOnlyList<string> DefaultScopes,
    IReadOnlyList<KeyValueItem> AuthorizeParameters,
    bool NeedsClientSecret,
    string? TenantLabel,
    string? TenantDefault,
    string? TenantHelp,
    string SetupSummary,
    string? ConsoleUrl,
    string? Caveat = null)
{
    /// <summary>True when this provider needs a tenant, directory or domain before its URLs mean anything.</summary>
    public bool NeedsTenant => TenantLabel is not null;

    /// <summary>Shown as the provider's label in the picker.</summary>
    public override string ToString() => DisplayName;

    /// <summary>
    /// The issuer for a given tenant, or null when this provider has no discovery document.
    ///
    /// <para>An empty tenant falls back to <see cref="TenantDefault"/> rather than producing a URL with
    /// a literal <c>{tenant}</c> in it: a half-substituted URL fails at the provider with an error
    /// about the request, which says nothing about the empty box that caused it.</para>
    /// </summary>
    public string? IssuerFor(string? tenant) => Substitute(IssuerTemplate, tenant);

    /// <summary>The authorize endpoint for a given tenant.</summary>
    public string? AuthorizeEndpointFor(string? tenant) => Substitute(AuthorizeEndpoint, tenant);

    /// <summary>The token endpoint for a given tenant.</summary>
    public string? TokenEndpointFor(string? tenant) => Substitute(TokenEndpoint, tenant);

    private string? Substitute(string? template, string? tenant)
    {
        if (template is null)
        {
            return null;
        }

        var value = string.IsNullOrWhiteSpace(tenant) ? TenantDefault : tenant.Trim();

        return value is null ? template : template.Replace("{tenant}", value, StringComparison.Ordinal);
    }
}
