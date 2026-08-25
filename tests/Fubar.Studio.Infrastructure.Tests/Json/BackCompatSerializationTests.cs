using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Tests.Json;

public class BackCompatSerializationTests
{
    [Fact]
    public void AppVariable_reads_legacy_isSecret_true_as_secret_kind()
    {
        var variable = JsonSerializer.Deserialize<AppVariable>("""{ "key": "apiKey", "isSecret": true }""", FubarJson.Options);

        Assert.NotNull(variable);
        Assert.Equal(VariableKind.Secret, variable.Kind);
    }

    [Fact]
    public void AppVariable_writes_kind_and_not_isSecret()
    {
        var json = JsonSerializer.Serialize(new AppVariable { Key = "apiKey", Kind = VariableKind.Session }, FubarJson.Options);

        Assert.Contains("\"kind\": \"session\"", json);
        Assert.DoesNotContain("isSecret", json);
        Assert.DoesNotContain("\"value\"", json); // Session/Secret values are never written to disk
    }

    [Fact]
    public void AuthConfig_without_new_fields_loads_as_legacy()
    {
        var json = """
        { "type": "oAuth2", "oAuth2Grant": "clientCredentials", "tokenUrl": "https://auth/token", "clientId": "c" }
        """;

        var auth = JsonSerializer.Deserialize<AuthConfig>(json, FubarJson.Options);

        Assert.NotNull(auth);
        Assert.Null(auth.TokenRequest);
        Assert.Empty(auth.TokenCaptures);
        Assert.Null(auth.ExpiresInExpression);
    }

    [Fact]
    public void AuthConfig_template_config_round_trips()
    {
        var original = new AuthConfig
        {
            Type = AuthType.OAuth2,
            TokenRequest = new AuthTokenRequest
            {
                Method = "POST",
                Url = "{{token_url}}",
                Body = new RequestBody
                {
                    Type = BodyType.UrlEncoded,
                    UrlEncoded = [new KeyValueItem { Key = "grant_type", Value = "client_credentials" }],
                },
            },
            TokenCaptures = [new CaptureRule { VariableName = "oauth2_access_token", Expression = "$.access_token", Scope = CaptureScope.Session }],
            ExpiresInExpression = "$.expires_in",
        };

        var json = JsonSerializer.Serialize(original, FubarJson.Options);
        var roundTripped = JsonSerializer.Deserialize<AuthConfig>(json, FubarJson.Options);

        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped.TokenRequest);
        Assert.Equal("{{token_url}}", roundTripped.TokenRequest!.Url);
        Assert.Equal(BodyType.UrlEncoded, roundTripped.TokenRequest.Body.Type);
        Assert.Equal("grant_type", roundTripped.TokenRequest.Body.UrlEncoded.Single().Key);
        Assert.Equal("$.access_token", Assert.Single(roundTripped.TokenCaptures).Expression);
        Assert.Equal("$.expires_in", roundTripped.ExpiresInExpression);
    }
}
