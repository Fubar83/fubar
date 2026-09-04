using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Protocols.Http;

namespace Fubar.Studio.Infrastructure.Tests.Protocols;

/// <summary>
/// Client certificate, private CA and proxy - the three things a corporate network asks for, and
/// without which the answer to "can it call our API" was no.
///
/// <para>The trap they had to avoid: clients are cached per (workspace, environment), so a transport
/// setting that did not take part in that key would let two environments share one handler and present
/// the wrong certificate. That is the same class of leak the per-environment cookie jar exists to
/// prevent.</para>
/// </summary>
public class TransportSettingsTests
{
    [Fact]
    public void Two_environments_with_different_certificates_get_different_clients()
    {
        using var provider = new ScopedHttpClientProvider();

        var a = provider.GetClient("ws::dev", new TransportSettings { ClientCertificateThumbprint = "AAAA" });
        var b = provider.GetClient("ws::dev", new TransportSettings { ClientCertificateThumbprint = "BBBB" });

        Assert.NotSame(a, b);
    }

    [Fact]
    public void The_same_settings_reuse_one_client()
    {
        using var provider = new ScopedHttpClientProvider();

        var a = provider.GetClient("ws::dev", new TransportSettings { ProxyUrl = "http://proxy:8080" });
        var b = provider.GetClient("ws::dev", new TransportSettings { ProxyUrl = "http://proxy:8080" });

        Assert.Same(a, b);
    }

    /// <summary>Empty settings must key identically to none at all, or every existing workspace would
    /// quietly get a second client the moment the section was added to the model.</summary>
    [Fact]
    public void Empty_settings_are_the_same_scope_as_none()
    {
        using var provider = new ScopedHttpClientProvider();

        Assert.Same(provider.GetClient("ws::dev"), provider.GetClient("ws::dev", new TransportSettings()));
    }

    [Fact]
    public void Different_scopes_still_get_different_clients()
    {
        using var provider = new ScopedHttpClientProvider();

        Assert.NotSame(provider.GetClient("ws::dev"), provider.GetClient("ws::prod"));
    }

    /// <summary>
    /// A thumbprint matching nothing is reported rather than ignored. Silently presenting no
    /// certificate produces a TLS failure much later that names neither the setting nor the
    /// certificate.
    /// </summary>
    [Fact]
    public void A_thumbprint_that_matches_nothing_is_reported()
    {
        var (_, problems) = TransportHandlerFactory.Create(
            new TransportSettings { ClientCertificateThumbprint = "0000000000000000000000000000000000000000" },
            workspaceRootPath: null);

        Assert.True(problems.Any);
        Assert.Contains("thumbprint", Assert.Single(problems.Messages), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unreadable_certificate_authority_is_reported()
    {
        var (_, problems) = TransportHandlerFactory.Create(
            new TransportSettings { CertificateAuthorityPaths = ["does-not-exist.pem"] },
            workspaceRootPath: Path.GetTempPath());

        Assert.True(problems.Any);
        Assert.Contains("certificate authority", Assert.Single(problems.Messages), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_invalid_proxy_url_is_reported_and_the_system_proxy_is_kept()
    {
        var (handler, problems) = TransportHandlerFactory.Create(
            new TransportSettings { ProxyUrl = "not a url" },
            workspaceRootPath: null);

        Assert.True(problems.Any);
        Assert.Null(handler.Proxy);
    }

    /// <summary>
    /// WebProxy.BypassList is a list of REGULAR EXPRESSIONS, which nothing about the setting suggests -
    /// every other proxy configuration takes "*.internal". Typing that threw a RegexParseException out
    /// of WebProxy the first time the proxy was used, killing the request with an error that named
    /// neither the proxy nor the setting. Found by this test, not in the field.
    /// </summary>
    [Fact]
    public void A_wildcard_bypass_entry_is_accepted_and_matches()
    {
        var (handler, problems) = TransportHandlerFactory.Create(
            new TransportSettings { ProxyUrl = "http://proxy.corp:8080", ProxyBypass = ["*.internal", "localhost"] },
            workspaceRootPath: null);

        Assert.False(problems.Any);
        Assert.True(handler.UseProxy);

        var proxy = Assert.IsType<System.Net.WebProxy>(handler.Proxy);
        Assert.True(proxy.IsBypassed(new Uri("https://api.internal/orders")));
        Assert.True(proxy.IsBypassed(new Uri("http://localhost:5000/")));
        Assert.False(proxy.IsBypassed(new Uri("https://api.example.com/")));
    }

    /// <summary>Anchored, so a bypass for "corp.com" does not also bypass "corp.com.evil.example".</summary>
    [Fact]
    public void A_bypass_entry_does_not_match_a_longer_host()
    {
        var (handler, _) = TransportHandlerFactory.Create(
            new TransportSettings { ProxyUrl = "http://proxy.corp:8080", ProxyBypass = ["corp.com"] },
            workspaceRootPath: null);

        var proxy = Assert.IsType<System.Net.WebProxy>(handler.Proxy);
        Assert.True(proxy.IsBypassed(new Uri("https://corp.com/")));
        Assert.False(proxy.IsBypassed(new Uri("https://corp.com.evil.example/")));
    }

    /// <summary>
    /// The defaults the rest of the pipeline depends on must survive a transport section: manual
    /// redirects are what let the executor drop credential headers on a cross-origin hop, and the
    /// cookie container is what keeps sessions per environment.
    /// </summary>
    [Fact]
    public void The_redirect_and_cookie_behaviour_is_unchanged_by_transport_settings()
    {
        var (handler, _) = TransportHandlerFactory.Create(
            new TransportSettings { ProxyUrl = "http://proxy.corp:8080" },
            workspaceRootPath: null);

        Assert.False(handler.AllowAutoRedirect);
        Assert.True(handler.UseCookies);
        Assert.NotNull(handler.CookieContainer);
    }
}
