using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Auth;

public class AuthTemplateCatalogTests
{
    [Fact]
    public void ClientCredentials_seeds_form_body_matching_the_legacy_fields()
    {
        var template = AuthTemplateCatalog.All.Single(t => t.Key == "oauth2-client-credentials");

        Assert.Equal("POST", template.SeedRequest.Method);
        Assert.Equal(BodyType.UrlEncoded, template.SeedRequest.Body.Type);

        var fields = template.SeedRequest.Body.UrlEncoded;
        Assert.Equal("client_credentials", fields.Single(f => f.Key == "grant_type").Value);
        Assert.Contains(fields, f => f.Key == "client_id");
        Assert.Contains(fields, f => f.Key == "client_secret");
        Assert.Contains(fields, f => f.Key == "scope");

        var capture = Assert.Single(template.SeedCaptures);
        Assert.Equal(AuthDefaults.AccessTokenVariable, capture.VariableName);
        Assert.Equal("$.access_token", capture.Expression);
        Assert.Equal(CaptureScope.Session, capture.Scope);
        Assert.Equal("$.expires_in", template.ExpiresInExpression);
    }

    [Fact]
    public void RefreshToken_seeds_grant_and_rotates_the_refresh_token_in_place()
    {
        var template = AuthTemplateCatalog.All.Single(t => t.Key == "oauth2-refresh-token");

        var fields = template.SeedRequest.Body.UrlEncoded;
        Assert.Equal("refresh_token", fields.Single(f => f.Key == "grant_type").Value);

        // The body reads the same variable the capture writes, so a rotated refresh token is reused.
        var refreshField = fields.Single(f => f.Key == "refresh_token").Value;
        var refreshCapture = template.SeedCaptures.Single(c => c.Expression == "$.refresh_token");
        Assert.Equal($"{{{{{refreshCapture.VariableName}}}}}", refreshField);
        Assert.Contains(template.SeedCaptures, c => c.VariableName == AuthDefaults.AccessTokenVariable);
    }

    [Fact]
    public void CustomLogin_seeds_a_json_body_and_a_token_capture()
    {
        var template = AuthTemplateCatalog.All.Single(t => t.Key == "custom-login");

        Assert.Null(template.Grant);
        Assert.Equal(BodyType.Json, template.SeedRequest.Body.Type);
        Assert.Equal("token", template.AccessTokenVariable);
        Assert.Null(template.ExpiresInExpression);
        Assert.Contains(template.SeedCaptures, c => c.VariableName == "token" && c.Expression == "$.token");
    }

    [Fact]
    public void All_captures_are_session_scoped()
    {
        Assert.All(AuthTemplateCatalog.All.SelectMany(t => t.SeedCaptures), c => Assert.Equal(CaptureScope.Session, c.Scope));
    }
}
