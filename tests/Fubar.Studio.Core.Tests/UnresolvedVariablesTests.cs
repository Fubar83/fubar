using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// Naming the variables that did not resolve.
///
/// This exists because of one specific, miserable failure: substitution leaves what it cannot resolve
/// exactly as it found it, so an OAuth token URL of <c>{{authHost}}/oauth/token</c> reaches the HTTP
/// client as that literal string and comes back as an invalid-URI error - or, worse, as a 404 from a
/// real server. The cause and the symptom are in different places and the symptom names the wrong
/// thing entirely.
/// </summary>
public class UnresolvedVariablesTests
{
    [Fact]
    public void Text_with_everything_resolved_reports_nothing()
    {
        Assert.Empty(UnresolvedVariables.In("https://auth.example.com/oauth/token"));
        Assert.Empty(UnresolvedVariables.In(""));
        Assert.Empty(UnresolvedVariables.In((string?)null));
    }

    [Fact]
    public void An_unresolved_token_is_named()
    {
        Assert.Equal(["authHost"], UnresolvedVariables.In("{{authHost}}/oauth/token"));
    }

    [Fact]
    public void The_same_variable_twice_is_named_once()
    {
        // The user has one thing to fix, so they should be told one thing.
        Assert.Equal(["host"], UnresolvedVariables.In("{{host}}/a/{{host}}/b"));
    }

    [Fact]
    public void Several_fields_are_reported_together()
    {
        // A token request with an unresolved host AND an unresolved client id should say both, rather
        // than sending the user round the loop once per variable.
        var names = UnresolvedVariables.In("{{authHost}}/token", "{{clientId}}", "secret-is-literal", null);

        Assert.Equal(["authHost", "clientId"], names);
    }

    [Fact]
    public void The_order_is_the_order_they_appear()
    {
        Assert.Equal(["b", "a"], UnresolvedVariables.In("{{b}} then {{a}}"));
    }

    [Fact]
    public void Text_that_merely_contains_braces_is_not_mistaken_for_a_variable()
    {
        // A JSON body in a token request is full of braces.
        Assert.Empty(UnresolvedVariables.In("""{"grant_type":"client_credentials"}"""));
        Assert.Empty(UnresolvedVariables.In("{{ spaced }}"));
        Assert.Empty(UnresolvedVariables.In("{{has-a-dash}}"));
    }

    [Fact]
    public void The_sentence_names_them_and_says_where_to_define_them()
    {
        var one = UnresolvedVariables.Describe(["authHost"]);

        Assert.Contains("{{authHost}}", one);
        Assert.Contains("active environment", one);

        var two = UnresolvedVariables.Describe(["authHost", "clientId"]);

        Assert.Contains("{{authHost}}, {{clientId}}", two);
        Assert.Contains("are not defined", two);
    }

    [Fact]
    public void Nothing_unresolved_produces_no_sentence()
    {
        // Null rather than an empty string, so a caller can use it as the condition itself.
        Assert.Null(UnresolvedVariables.Describe([]));
        Assert.Null(UnresolvedVariables.Describe(UnresolvedVariables.In("no variables here")));
    }
}
