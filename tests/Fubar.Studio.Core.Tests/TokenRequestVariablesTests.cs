using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// Which variables a token request reads, and which of them exist.
///
/// The per-field tooltip already tints one box at a time, which tells you about the box you happen to
/// be hovering. Setting OAuth up needs the list instead, because the variable you have not defined is
/// almost always in a field you are not currently looking at.
/// </summary>
public class TokenRequestVariablesTests
{
    /// <summary>Stands in for the resolver: defined names substitute, everything else is left alone.</summary>
    private static Func<string, string> Defines(params string[] names) =>
        text =>
        {
            foreach (var name in names)
            {
                text = text.Replace($"{{{{{name}}}}}", $"value-of-{name}", StringComparison.Ordinal);
            }

            return text;
        };

    [Fact]
    public void A_request_with_no_variables_reports_none()
    {
        var request = new AuthTokenRequest { Url = "https://auth.example.com/token" };

        Assert.Empty(TokenRequestVariables.Of(request, Defines()));
        Assert.Null(TokenRequestVariables.Describe([]));
    }

    [Fact]
    public void Null_is_tolerated()
    {
        // A profile that has never been through the editor has no token request at all.
        Assert.Empty(TokenRequestVariables.Of(null, Defines()));
    }

    [Fact]
    public void The_url_is_read()
    {
        var request = new AuthTokenRequest { Url = "{{authHost}}/token" };

        var found = Assert.Single(TokenRequestVariables.Of(request, Defines()));

        Assert.Equal("authHost", found.Name);
        Assert.False(found.IsResolved);
    }

    [Fact]
    public void A_defined_variable_is_marked_resolved()
    {
        var request = new AuthTokenRequest { Url = "{{authHost}}/token" };

        Assert.True(Assert.Single(TokenRequestVariables.Of(request, Defines("authHost"))).IsResolved);
    }

    [Fact]
    public void Headers_and_body_fields_are_read_too()
    {
        // The missing variable is usually in a field you are not looking at - which is the whole
        // reason this list exists rather than relying on the per-box tint.
        var request = new AuthTokenRequest
        {
            Url = "{{authHost}}/token",
            Headers = [new KeyValueItem { Key = "X-Tenant", Value = "{{tenant}}" }],
            Body = new RequestBody
            {
                Type = BodyType.UrlEncoded,
                UrlEncoded =
                [
                    new KeyValueItem { Key = "client_id", Value = "{{clientId}}" },
                    new KeyValueItem { Key = "client_secret", Value = "{{clientSecret}}" },
                ],
            },
        };

        var names = TokenRequestVariables.Of(request, Defines("authHost", "tenant")).ToList();

        Assert.Equal(["authHost", "tenant", "clientId", "clientSecret"], names.Select(v => v.Name));
        Assert.Equal([true, true, false, false], names.Select(v => v.IsResolved));
    }

    [Fact]
    public void A_raw_json_body_is_read()
    {
        var request = new AuthTokenRequest
        {
            Url = "https://auth/token",
            Body = new RequestBody { Type = BodyType.Json, Raw = """{"client_id":"{{clientId}}"}""" },
        };

        Assert.Equal("clientId", Assert.Single(TokenRequestVariables.Of(request, Defines())).Name);
    }

    [Fact]
    public void The_same_variable_in_two_places_is_listed_once()
    {
        var request = new AuthTokenRequest
        {
            Url = "{{host}}/token",
            Headers = [new KeyValueItem { Key = "Origin", Value = "{{host}}" }],
        };

        Assert.Single(TokenRequestVariables.Of(request, Defines()));
    }

    [Fact]
    public void The_summary_names_only_what_is_missing()
    {
        var request = new AuthTokenRequest
        {
            Url = "{{authHost}}/token",
            Headers = [new KeyValueItem { Key = "X-Id", Value = "{{clientId}}" }],
        };

        var summary = TokenRequestVariables.Describe(TokenRequestVariables.Of(request, Defines("authHost")));

        Assert.Equal("Not defined: {{clientId}}", summary);
    }

    [Fact]
    public void Everything_defined_says_so_rather_than_saying_nothing()
    {
        // Silence would be ambiguous: it reads the same as "this request uses no variables", and the
        // difference matters when you are working out why a token request is failing.
        var request = new AuthTokenRequest { Url = "{{authHost}}/token" };

        Assert.Equal(
            "All 1 variable(s) this request uses are defined.",
            TokenRequestVariables.Describe(TokenRequestVariables.Of(request, Defines("authHost"))));
    }
}
