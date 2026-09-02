using System.Net.Http;
using Fubar.Studio.Core.Auth;

namespace Fubar.Studio.Infrastructure.Auth;

/// <summary>
/// ADAPTER. Fetches a discovery document over HTTP and hands the body to
/// <see cref="OpenIdDiscovery.Parse"/>.
///
/// Deliberately thin, and deliberately forgiving: every failure here is something the user can fix by
/// correcting what they pasted, so each one comes back as a sentence rather than an exception. A
/// discovery attempt that throws would be worse than the copy-from-the-docs it replaces.
/// </summary>
public sealed class OpenIdDiscoveryService : IOpenIdDiscoveryService
{
    private readonly IHttpClientFactory _clients;

    public OpenIdDiscoveryService(IHttpClientFactory clients) => _clients = clients;

    public async Task<DiscoveryResult> DiscoverAsync(string? issuerOrUrl, CancellationToken cancellationToken = default)
    {
        if (OpenIdDiscovery.WellKnownUrlFor(issuerOrUrl) is not { } url)
        {
            return new DiscoveryResult(null, "Enter the provider's issuer URL first.");
        }

        try
        {
            using var client = _clients.CreateClient(nameof(OpenIdDiscoveryService));

            // Short: this runs on a button the user is waiting on, and a provider that has not
            // answered in ten seconds is not going to be the fast path to a working setup.
            client.Timeout = TimeSpan.FromSeconds(10);

            using var response = await client.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DiscoveryResult(null, $"{url} returned {(int)response.StatusCode}.");
            }

            if (OpenIdDiscovery.Parse(body) is not { } configuration)
            {
                return new DiscoveryResult(null, $"{url} did not return an OpenID configuration with a token endpoint.");
            }

            return new DiscoveryResult(
                configuration,
                $"Found {configuration.Issuer ?? url}.");
        }
        catch (TaskCanceledException)
        {
            return new DiscoveryResult(null, $"{url} did not respond.");
        }
        catch (HttpRequestException ex)
        {
            return new DiscoveryResult(null, $"Could not reach {url}: {ex.Message}");
        }
    }
}
