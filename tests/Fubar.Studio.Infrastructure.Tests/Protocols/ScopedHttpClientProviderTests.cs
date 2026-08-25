using Fubar.Studio.Infrastructure.Protocols.Http;

namespace Fubar.Studio.Infrastructure.Tests.Protocols;

public class ScopedHttpClientProviderTests
{
    [Fact]
    public void Same_scope_returns_the_same_client()
    {
        using var provider = new ScopedHttpClientProvider();

        Assert.Same(provider.GetClient("ws::dev"), provider.GetClient("ws::dev"));
    }

    [Fact]
    public void Different_scopes_get_different_clients_and_cookie_jars()
    {
        using var provider = new ScopedHttpClientProvider();

        var dev = provider.GetClient("ws::dev");
        var prod = provider.GetClient("ws::prod");

        // Distinct clients => distinct HttpClientHandler/CookieContainer => cookies never cross environments.
        Assert.NotSame(dev, prod);
    }
}
