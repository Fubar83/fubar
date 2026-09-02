namespace Fubar.Studio.Core.Auth;

/// <summary>What a discovery attempt produced.</summary>
/// <param name="Configuration">The document, or null when there was not a usable one.</param>
/// <param name="Message">What to tell the user - the issuer that answered, or why nothing did.</param>
public sealed record DiscoveryResult(OpenIdConfiguration? Configuration, string Message)
{
    public bool Ok => Configuration is not null;
}

/// <summary>
/// PORT. Fetches a provider's <c>/.well-known/openid-configuration</c>.
///
/// A port because it is the only outbound HTTP call the auth EDITOR makes on its own - everything else
/// it does goes through the request executor - and because a view model must not reach for a socket.
/// Parsing lives in <see cref="OpenIdDiscovery"/>, pure, so the awkward answers are testable without
/// a network.
/// </summary>
public interface IOpenIdDiscoveryService
{
    /// <summary>
    /// Fetches and parses. <paramref name="issuerOrUrl"/> may be the issuer, the issuer with a
    /// trailing slash, or the well-known URL itself - see <see cref="OpenIdDiscovery.WellKnownUrlFor"/>.
    /// </summary>
    Task<DiscoveryResult> DiscoverAsync(string? issuerOrUrl, CancellationToken cancellationToken = default);
}
