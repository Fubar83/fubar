using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// A named preset for the request-builder-style auth editor: a seed token request plus the capture rules
/// and variable names that make it work. Applying a template copies <see cref="SeedRequest"/>/
/// <see cref="SeedCaptures"/> into the editable state - nothing is locked afterward, so the user can add
/// headers, change the body, or edit the captures. Built-ins are exposed by <see cref="AuthTemplateCatalog"/>.
/// </summary>
/// <param name="Key">Stable identifier (used for selection / round-tripping).</param>
/// <param name="DisplayName">Human-facing name shown in the template picker.</param>
/// <param name="Grant">The OAuth2 grant this reproduces, or <c>null</c> for a non-OAuth custom login.</param>
/// <param name="SeedRequest">The method/URL/headers/body the editor starts from.</param>
/// <param name="SeedCaptures">The JSONPath → session-variable rules the editor starts from.</param>
/// <param name="AccessTokenVariable">The session variable the <c>Authorization: Bearer</c> header reads.</param>
/// <param name="ExpiryVariable">The session variable holding the token's expiry (unix seconds).</param>
/// <param name="ExpiresInExpression">JSONPath to relative <c>expires_in</c> seconds, or <c>null</c> for none.</param>
/// <param name="AuthorizeUrl">
/// The browser half's endpoint, for the grants that have one. Empty for the rest, and for the generic
/// authorization-code template, which cannot know it - that is what Discover and the provider presets
/// are for.
/// </param>
/// <param name="AuthorizeParameters">
/// Extra query parameters for the authorize URL. Part of the template rather than typed each time
/// because they are provider facts, not user choices - see <see cref="SignInProvider"/>.
/// </param>
/// <param name="ProviderKey">
/// The <see cref="SignInProvider"/> this was seeded from, or null for a hand-built template. Persisted
/// so a saved profile reopens on the provider it was set up for.
/// </param>
public sealed record AuthTemplate(
    string Key,
    string DisplayName,
    OAuth2GrantType? Grant,
    AuthTokenRequest SeedRequest,
    IReadOnlyList<CaptureRule> SeedCaptures,
    string AccessTokenVariable,
    string ExpiryVariable,
    string? ExpiresInExpression,
    string AuthorizeUrl = "",
    IReadOnlyList<KeyValueItem>? AuthorizeParameters = null,
    string? ProviderKey = null)
{
    /// <summary>Never null, so callers need not decide what an absent list means.</summary>
    public IReadOnlyList<KeyValueItem> AuthorizeParameters { get; init; } = AuthorizeParameters ?? [];


    /// <summary>Shown as the template's label in the picker.</summary>
    public override string ToString() => DisplayName;
}
