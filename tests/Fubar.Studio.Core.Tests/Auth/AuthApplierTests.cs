using System.Text;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Auth;

public class AuthApplierTests
{
    [Fact]
    public void Bearer_builds_authorization_header()
    {
        var applied = AuthApplier.Build(new ResolvedAuth(AuthType.Bearer, Token: "abc", null, null, null, ApiKeyLocation.Header, null, null));

        var header = Assert.Single(applied.Headers);
        Assert.Equal("Authorization", header.Key);
        Assert.Equal("Bearer abc", header.Value);
        Assert.Empty(applied.QueryParams);
    }

    [Fact]
    public void OAuth2_builds_bearer_header_from_acquired_token()
    {
        var applied = AuthApplier.Build(new ResolvedAuth(AuthType.OAuth2, null, AccessToken: "tok-123", null, null, ApiKeyLocation.Header, null, null));

        Assert.Equal("Bearer tok-123", Assert.Single(applied.Headers).Value);
    }

    [Fact]
    public void OAuth2_without_a_token_applies_nothing()
    {
        var applied = AuthApplier.Build(new ResolvedAuth(AuthType.OAuth2, null, AccessToken: null, null, null, ApiKeyLocation.Header, null, null));

        Assert.True(applied.IsEmpty);
    }

    [Fact]
    public void Basic_builds_base64_authorization_header_after_resolution()
    {
        var applied = AuthApplier.Build(new ResolvedAuth(AuthType.Basic, null, null, null, null, ApiKeyLocation.Header, Username: "user", Password: "pass"));

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        Assert.Equal(expected, Assert.Single(applied.Headers).Value);
    }

    [Fact]
    public void ApiKey_header_goes_to_headers()
    {
        var applied = AuthApplier.Build(new ResolvedAuth(AuthType.ApiKey, null, null, ApiKeyName: "X-Api-Key", ApiKeyValue: "k", ApiKeyLocation.Header, null, null));

        var header = Assert.Single(applied.Headers);
        Assert.Equal("X-Api-Key", header.Key);
        Assert.Equal("k", header.Value);
        Assert.Empty(applied.QueryParams);
    }

    [Fact]
    public void ApiKey_query_goes_to_query_params()
    {
        var applied = AuthApplier.Build(new ResolvedAuth(AuthType.ApiKey, null, null, ApiKeyName: "api_key", ApiKeyValue: "k", ApiKeyLocation.QueryParam, null, null));

        var param = Assert.Single(applied.QueryParams);
        Assert.Equal("api_key", param.Key);
        Assert.Equal("k", param.Value);
        Assert.Empty(applied.Headers);
    }

    [Fact]
    public void Preview_masks_secrets_and_shows_oauth_token_reference()
    {
        Assert.Equal("Bearer {{oauth2_access_token}}",
            Assert.Single(AuthApplier.BuildPreview(new AuthConfig { Type = AuthType.OAuth2 }).Headers).Value);

        Assert.Equal("Basic ••••••••",
            Assert.Single(AuthApplier.BuildPreview(new AuthConfig { Type = AuthType.Basic, Username = "u", Password = "p" }).Headers).Value);

        var apiKeyPreview = AuthApplier.BuildPreview(new AuthConfig
        {
            Type = AuthType.ApiKey,
            ApiKeyName = "X-Api-Key",
            ApiKeyValue = "s3cret",
            ApiKeyLocation = ApiKeyLocation.Header,
        });
        Assert.Equal("••••••••", Assert.Single(apiKeyPreview.Headers).Value);
    }

    [Fact]
    public void Preview_keeps_a_variable_reference_visible()
    {
        var preview = AuthApplier.BuildPreview(new AuthConfig { Type = AuthType.Bearer, Token = "{{my_token}}" });

        Assert.Equal("Bearer {{my_token}}", Assert.Single(preview.Headers).Value);
    }
}
