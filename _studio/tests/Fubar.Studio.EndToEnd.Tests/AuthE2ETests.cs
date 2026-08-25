using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.EndToEnd.Tests;

/// <summary>Live end-to-end: each auth mode is applied to a real request against httpbin.org, which echoes
/// back what it received - proving the credential actually reached the wire. Examples of every supported
/// no-browser scheme, plus rejection (401) and variable-resolved cases. Opt-in via FUBAR_E2E=1.</summary>
public class AuthE2ETests
{
    [Fact]
    public async Task Bearer_token_reaches_the_server()
    {
        HttpBin.RequireLive();

        var result = await HttpBin.Send(
            new AuthConfig { Type = AuthType.Bearer, Token = "abc123" },
            HttpBin.Get($"{HttpBin.BaseUrl}/bearer"));

        Assert.Equal(200, result.StatusCode);       // /bearer returns 401 if no bearer was sent
        Assert.Contains("abc123", result.Body);     // and echoes the token back
    }

    [Fact]
    public async Task Missing_bearer_is_rejected()
    {
        HttpBin.RequireLive();

        var result = await HttpBin.Send(auth: null, HttpBin.Get($"{HttpBin.BaseUrl}/bearer"));

        Assert.Equal(401, result.StatusCode);        // no credential sent -> unauthorized
    }

    [Fact]
    public async Task Basic_auth_reaches_the_server()
    {
        HttpBin.RequireLive();

        var result = await HttpBin.Send(
            new AuthConfig { Type = AuthType.Basic, Username = "user", Password = "passwd" },
            HttpBin.Get($"{HttpBin.BaseUrl}/basic-auth/user/passwd"));

        Assert.Equal(200, result.StatusCode);        // 401 unless the base64 Basic header was built + sent
    }

    [Fact]
    public async Task Basic_auth_with_a_wrong_password_is_rejected()
    {
        HttpBin.RequireLive();

        var result = await HttpBin.Send(
            new AuthConfig { Type = AuthType.Basic, Username = "user", Password = "wrong" },
            HttpBin.Get($"{HttpBin.BaseUrl}/basic-auth/user/passwd"));

        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task ApiKey_in_a_header_reaches_the_server()
    {
        HttpBin.RequireLive();

        var result = await HttpBin.Send(
            new AuthConfig { Type = AuthType.ApiKey, ApiKeyName = "X-Api-Key", ApiKeyValue = "s3cret", ApiKeyLocation = ApiKeyLocation.Header },
            HttpBin.Get($"{HttpBin.BaseUrl}/headers"));

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("s3cret", result.Body);      // echoed under "headers"
    }

    [Fact]
    public async Task ApiKey_in_the_query_reaches_the_server()
    {
        HttpBin.RequireLive();

        var result = await HttpBin.Send(
            new AuthConfig { Type = AuthType.ApiKey, ApiKeyName = "api_key", ApiKeyValue = "s3cret", ApiKeyLocation = ApiKeyLocation.QueryParam },
            HttpBin.Get($"{HttpBin.BaseUrl}/get"));

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("s3cret", result.Body);      // echoed under "args"
    }

    [Fact]
    public async Task Auth_credential_from_an_environment_variable_is_resolved_and_sent()
    {
        HttpBin.RequireLive();

        var env = new WorkspaceEnvironment
        {
            Name = "Dev",
            Variables = [new AppVariable { Key = "token", Value = "env-tok-9f2", Kind = VariableKind.Normal }],
        };

        var result = await HttpBin.Send(
            new AuthConfig { Type = AuthType.Bearer, Token = "{{token}}" },
            HttpBin.Get($"{HttpBin.BaseUrl}/bearer"),
            env);

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("env-tok-9f2", result.Body); // {{token}} resolved from the active environment, then sent
    }

    [Fact]
    public async Task OAuth2_template_acquires_a_token_then_sends_it()
    {
        HttpBin.RequireLive();

        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            // The "token endpoint": /response-headers echoes its query params in the JSON body, so we can
            // capture a token from it without a real identity provider.
            TokenRequest = new AuthTokenRequest { Method = "GET", Url = $"{HttpBin.BaseUrl}/response-headers?access_token=tok-xyz" },
            TokenCaptures = [new CaptureRule { VariableName = AuthDefaults.AccessTokenVariable, Expression = "$.access_token" }],
        };

        var result = await HttpBin.Send(auth, HttpBin.Get($"{HttpBin.BaseUrl}/bearer"));

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("tok-xyz", result.Body);     // token captured from the token request, then sent as Bearer
    }
}
