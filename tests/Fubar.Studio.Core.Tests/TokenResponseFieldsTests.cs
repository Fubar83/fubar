using Fubar.Studio.Core.Auth;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// Reading a token response into the paths you could capture from it.
///
/// The step this exists for is the worst one in setting OAuth up: a capture rule needs a JSONPath like
/// $.access_token, and the response it addresses was never shown - so the one step needing exact
/// knowledge of the payload was the one step with no way to see it.
/// </summary>
public class TokenResponseFieldsTests
{
    private const string Typical =
        """{"access_token":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.payload.sig","token_type":"Bearer","expires_in":3600,"scope":"read write"}""";

    [Fact]
    public void A_typical_response_yields_a_path_per_field()
    {
        var fields = TokenResponseFields.From(Typical);

        Assert.Equal(
            ["$.access_token", "$.expires_in", "$.token_type", "$.scope"],
            fields.Select(f => f.Path));
    }

    [Fact]
    public void The_credential_field_comes_first()
    {
        // A provider that returns twenty fields should still put access_token at the top.
        var fields = TokenResponseFields.From(
            """{"issued_at":"1","scope":"read","access_token":"abcdefghijklmnop","foo":"bar"}""");

        Assert.Equal("$.access_token", fields[0].Path);
    }

    [Fact]
    public void The_token_value_itself_is_never_shown_in_full()
    {
        // Showing the response is for finding the FIELD. A preview pane that spills a live credential
        // into a screenshot is a bad trade for information nobody needed.
        var token = TokenResponseFields.From(Typical).Single(f => f.Path == "$.access_token");

        Assert.DoesNotContain("payload", token.Preview);
        Assert.Contains("chars", token.Preview);
    }

    [Fact]
    public void Ordinary_values_are_shown_as_they_are()
    {
        var fields = TokenResponseFields.From(Typical);

        Assert.Equal("3600", fields.Single(f => f.Path == "$.expires_in").Preview);
        Assert.Equal("Bearer", fields.Single(f => f.Path == "$.token_type").Preview);
        Assert.Equal("read write", fields.Single(f => f.Path == "$.scope").Preview);
    }

    [Fact]
    public void One_level_of_nesting_is_reached()
    {
        var fields = TokenResponseFields.From("""{"data":{"access_token":"abc"},"ok":true}""");

        Assert.Contains("$.data.access_token", fields.Select(f => f.Path));
    }

    [Fact]
    public void A_response_that_is_not_a_JSON_object_yields_nothing()
    {
        // A token endpoint returning HTML - a login page, a proxy error - is completely ordinary, and
        // not something to throw over.
        Assert.Empty(TokenResponseFields.From("<html><body>Sign in</body></html>"));
        Assert.Empty(TokenResponseFields.From("[1,2,3]"));
        Assert.Empty(TokenResponseFields.From(""));
        Assert.Empty(TokenResponseFields.From(null));
    }

    [Fact]
    public void An_error_response_is_still_readable()
    {
        // The case you most need to see: the endpoint said no, and why.
        var fields = TokenResponseFields.From("""{"error":"invalid_client","error_description":"Bad secret"}""");

        Assert.Equal("Bad secret", fields.Single(f => f.Path == "$.error_description").Preview);
    }

    [Fact]
    public void The_obvious_captures_are_the_ones_offered()
    {
        var suggested = TokenResponseFields.Suggested(
            TokenResponseFields.From("""{"access_token":"a","refresh_token":"r","expires_in":60,"scope":"x"}"""));

        Assert.Equal(["$.access_token", "$.refresh_token"], suggested.Select(f => f.Path));
    }
}
