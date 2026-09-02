using Fubar.Studio.Core.Auth;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// Reading a provider's OpenID Connect discovery document.
///
/// Setting OAuth up starts with copying a token endpoint out of a provider's documentation. Every OIDC
/// provider publishes that endpoint at a predictable URL, so pasting the issuer should be enough.
/// </summary>
public class OpenIdDiscoveryTests
{
    private const string Document = """
        {
          "issuer": "https://login.example.com",
          "authorization_endpoint": "https://login.example.com/oauth2/authorize",
          "token_endpoint": "https://login.example.com/oauth2/token",
          "scopes_supported": ["openid", "profile", "api.read"],
          "grant_types_supported": ["authorization_code", "client_credentials", "refresh_token"]
        }
        """;

    // ---- Working out what to fetch ---------------------------------------------------------------

    [Theory]
    [InlineData("https://login.example.com")]
    [InlineData("https://login.example.com/")]
    [InlineData("login.example.com")]
    [InlineData("https://login.example.com/.well-known/openid-configuration")]
    public void Anything_a_provider_calls_the_issuer_resolves_to_the_same_url(string pasted)
    {
        // People paste all four. Being told "that is not a discovery URL" when you pasted the thing
        // the provider's own page calls the issuer is not help.
        Assert.Equal(
            "https://login.example.com/.well-known/openid-configuration",
            OpenIdDiscovery.WellKnownUrlFor(pasted));
    }

    [Fact]
    public void Nothing_pasted_means_nothing_to_fetch()
    {
        Assert.Null(OpenIdDiscovery.WellKnownUrlFor(null));
        Assert.Null(OpenIdDiscovery.WellKnownUrlFor("   "));
    }

    // ---- Reading it ------------------------------------------------------------------------------

    [Fact]
    public void The_endpoints_scopes_and_grants_are_read()
    {
        var config = OpenIdDiscovery.Parse(Document);

        Assert.Equal("https://login.example.com/oauth2/token", config!.TokenEndpoint);
        Assert.Equal("https://login.example.com/oauth2/authorize", config.AuthorizationEndpoint);
        Assert.Equal("https://login.example.com", config.Issuer);
        Assert.Equal(["openid", "profile", "api.read"], config.ScopesSupported);
        Assert.Contains("client_credentials", config.GrantTypesSupported);
    }

    [Fact]
    public void A_document_with_no_token_endpoint_is_not_usable()
    {
        // It cannot help with the one thing discovery is for, and reporting success would fill the
        // editor with nothing while saying it worked.
        Assert.Null(OpenIdDiscovery.Parse("""{"issuer":"https://x","scopes_supported":["openid"]}"""));
    }

    [Fact]
    public void Anything_that_is_not_a_discovery_document_returns_null_rather_than_throwing()
    {
        // The commonest failures are a typo'd issuer answering with an HTML 404 page, and a provider
        // that is simply not OIDC. Neither is exceptional.
        Assert.Null(OpenIdDiscovery.Parse("<html><title>404</title></html>"));
        Assert.Null(OpenIdDiscovery.Parse("[]"));
        Assert.Null(OpenIdDiscovery.Parse(""));
        Assert.Null(OpenIdDiscovery.Parse(null));
    }

    [Fact]
    public void Missing_optional_fields_are_empty_rather_than_fatal()
    {
        // A provider that publishes only a token endpoint is still worth reading - that is the field
        // people came for.
        var config = OpenIdDiscovery.Parse("""{"token_endpoint":"https://x/token"}""");

        Assert.Equal("https://x/token", config!.TokenEndpoint);
        Assert.Empty(config.ScopesSupported);
        Assert.Empty(config.GrantTypesSupported);
        Assert.Null(config.AuthorizationEndpoint);
    }
}
