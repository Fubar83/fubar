using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Auth;

public class OAuth2LegacyTemplateTests
{
    [Fact]
    public void ClientCredentials_maps_url_and_body_fields()
    {
        var legacy = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2Grant = OAuth2GrantType.ClientCredentials,
            TokenUrl = "https://auth/token",
            ClientId = "cid",
            ClientSecret = "csec",
            Scopes = "read write",
        };

        var (request, captures) = OAuth2LegacyTemplate.FromLegacy(legacy);

        Assert.Equal("POST", request.Method);
        Assert.Equal("https://auth/token", request.Url);
        Assert.Equal(BodyType.UrlEncoded, request.Body.Type);

        var fields = request.Body.UrlEncoded;
        Assert.Equal("client_credentials", fields.Single(f => f.Key == "grant_type").Value);
        Assert.Equal("cid", fields.Single(f => f.Key == "client_id").Value);
        Assert.Equal("csec", fields.Single(f => f.Key == "client_secret").Value);
        Assert.Equal("read write", fields.Single(f => f.Key == "scope").Value);

        var capture = Assert.Single(captures);
        Assert.Equal(AuthDefaults.AccessTokenVariable, capture.VariableName);
        Assert.Equal("$.access_token", capture.Expression);
        Assert.Equal(CaptureScope.Session, capture.Scope);
    }

    [Fact]
    public void RefreshGrant_includes_the_refresh_token_field()
    {
        var legacy = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2Grant = OAuth2GrantType.RefreshToken,
            TokenUrl = "https://auth/token",
            RefreshToken = "the-refresh",
        };

        var (request, _) = OAuth2LegacyTemplate.FromLegacy(legacy);

        var fields = request.Body.UrlEncoded;
        Assert.Equal("refresh_token", fields.Single(f => f.Key == "grant_type").Value);
        Assert.Equal("the-refresh", fields.Single(f => f.Key == "refresh_token").Value);
    }

    [Fact]
    public void Uses_the_configs_custom_access_token_variable_for_the_capture()
    {
        var legacy = new AuthConfig
        {
            Type = AuthType.OAuth2,
            TokenUrl = "https://auth/token",
            AccessTokenVariable = "my_token",
        };

        var (_, captures) = OAuth2LegacyTemplate.FromLegacy(legacy);

        Assert.Equal("my_token", Assert.Single(captures).VariableName);
    }

    [Fact]
    public void BasicHeader_client_auth_is_normalized_to_body_fields()
    {
        var legacy = new AuthConfig
        {
            Type = AuthType.OAuth2,
            TokenUrl = "https://auth/token",
            ClientId = "cid",
            ClientSecret = "csec",
            ClientAuthentication = OAuth2ClientAuth.BasicHeader,
        };

        var (request, _) = OAuth2LegacyTemplate.FromLegacy(legacy);

        // Client credentials go in the body (variable-safe) rather than a pre-encoded Basic header.
        Assert.Contains(request.Body.UrlEncoded, f => f.Key == "client_id" && f.Value == "cid");
        Assert.DoesNotContain(request.Headers, h => h.Key == "Authorization");
    }
}
