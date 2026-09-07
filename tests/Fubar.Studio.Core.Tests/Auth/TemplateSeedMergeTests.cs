using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Auth;

/// <summary>
/// What survives when a template is applied over work already done.
///
/// <para>Applying used to replace everything, so the natural order of setting OAuth up - Discover your
/// endpoints, fill in your client id, then change your mind about the grant - threw all of it away,
/// and pressing Apply a second time after correcting one field threw away the correction. Every test
/// here is a thing someone typed that must still be there afterwards.</para>
/// </summary>
public class TemplateSeedMergeTests
{
    private static KeyValueItem Field(string key, string value, bool enabled = true) =>
        new() { Key = key, Value = value, Enabled = enabled };

    private static CaptureRule Capture(string variable, string expression) =>
        new() { VariableName = variable, Expression = expression, Source = ResponseField.JsonBody };

    // ---- what counts as "the template does not know" ----------------------------------------------

    [Theory]
    [InlineData("{{token_url}}")]
    [InlineData("{{client_id}}")]
    [InlineData("  {{client_secret}}  ")]
    public void A_whole_value_placeholder_is_the_template_saying_it_does_not_know(string value) =>
        Assert.True(TemplateSeedMerge.IsPlaceholder(value));

    [Theory]
    [InlineData("https://auth.example.com/token")]
    [InlineData("{{BaseUrl}}/oauth/token")]
    [InlineData("authorization_code")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_a_real_answer(string? value) =>
        Assert.False(TemplateSeedMerge.IsPlaceholder(value));

    [Fact]
    public void A_composed_url_is_not_a_placeholder()
    {
        // {{BaseUrl}}/oauth/token is exactly the kind of URL people work hardest to get right.
        // Treating it as a placeholder would delete it.
        Assert.Equal(
            "{{BaseUrl}}/oauth/token",
            TemplateSeedMerge.Url("{{BaseUrl}}/oauth/token", "{{token_url}}"));
    }

    // ---- URLs --------------------------------------------------------------------------------------

    [Fact]
    public void A_placeholder_never_overwrites_an_endpoint_that_discover_found()
    {
        // The exact sequence that used to lose work: press Discover, get your token endpoint, then
        // change the grant.
        Assert.Equal(
            "https://login.example.com/oauth/token",
            TemplateSeedMerge.Url("https://login.example.com/oauth/token", "{{token_url}}"));
    }

    [Fact]
    public void An_empty_template_url_does_not_clear_one_either()
    {
        Assert.Equal("https://login.example.com/authorize", TemplateSeedMerge.Url("https://login.example.com/authorize", ""));
    }

    [Fact]
    public void A_providers_real_endpoint_does_overwrite()
    {
        // The entire point of choosing a provider. Only a template that KNOWS the endpoint replaces one.
        Assert.Equal(
            "https://oauth2.googleapis.com/token",
            TemplateSeedMerge.Url("https://old.example.com/token", "https://oauth2.googleapis.com/token"));
    }

    [Fact]
    public void An_empty_editor_takes_whatever_the_template_offers()
    {
        Assert.Equal("{{token_url}}", TemplateSeedMerge.Url("", "{{token_url}}"));
    }

    // ---- body fields, headers, authorize parameters -------------------------------------------------

    [Fact]
    public void A_client_id_typed_as_a_literal_survives()
    {
        // The headline complaint. The template ships {{client_id}} because it cannot know yours.
        var merged = TemplateSeedMerge.Fields(
            [Field("client_id", "123-abc.apps.googleusercontent.com")],
            [Field("grant_type", "authorization_code"), Field("client_id", "{{client_id}}")]);

        Assert.Equal("123-abc.apps.googleusercontent.com", merged.Single(f => f.Key == "client_id").Value);
    }

    [Fact]
    public void A_client_id_pointed_at_a_different_variable_survives_too()
    {
        // {{google_client_id}} instead of {{client_id}} is a deliberate choice, not a value to reset.
        var merged = TemplateSeedMerge.Fields(
            [Field("client_id", "{{google_client_id}}")],
            [Field("client_id", "{{client_id}}")]);

        Assert.Equal("{{google_client_id}}", merged.Single().Value);
    }

    [Fact]
    public void A_protocol_constant_is_the_templates_to_set()
    {
        // grant_type is a literal, not a placeholder - so switching from client credentials to an
        // authorization code actually changes the grant, which is the whole reason to apply a template.
        var merged = TemplateSeedMerge.Fields(
            [Field("grant_type", "client_credentials")],
            [Field("grant_type", "authorization_code")]);

        Assert.Equal("authorization_code", merged.Single().Value);
    }

    [Fact]
    public void A_field_the_template_knows_nothing_about_is_kept()
    {
        // A parameter someone's provider needs and nothing here has heard of.
        var merged = TemplateSeedMerge.Fields(
            [Field("audience", "https://api.example.com")],
            [Field("grant_type", "authorization_code")]);

        Assert.Equal("https://api.example.com", merged.Single(f => f.Key == "audience").Value);
    }

    [Fact]
    public void Template_fields_come_first_and_in_the_templates_order()
    {
        // So the request reads the way the template intended rather than in the order edits happened.
        var merged = TemplateSeedMerge.Fields(
            [Field("audience", "api"), Field("client_id", "mine")],
            [Field("grant_type", "authorization_code"), Field("client_id", "{{client_id}}")]);

        Assert.Equal(["grant_type", "client_id", "audience"], merged.Select(f => f.Key));
    }

    [Fact]
    public void A_row_switched_off_stays_off()
    {
        // Unticking is how someone tests whether a parameter is the problem. Re-enabling it silently
        // would undo the experiment they are in the middle of.
        var merged = TemplateSeedMerge.Fields(
            [Field("prompt", "consent", enabled: false)],
            [Field("prompt", "{{prompt}}")]);

        Assert.False(merged.Single().Enabled);
    }

    [Fact]
    public void Keys_match_regardless_of_case()
    {
        // Accept and accept are one header. Treating them as two would quietly send both.
        var merged = TemplateSeedMerge.Fields(
            [Field("accept", "application/xml")],
            [Field("Accept", "application/json")]);

        Assert.Single(merged);
    }

    [Fact]
    public void Nothing_is_lost_when_there_was_nothing_there()
    {
        var merged = TemplateSeedMerge.Fields([], [Field("grant_type", "authorization_code")]);

        Assert.Equal("authorization_code", merged.Single().Value);
    }

    // ---- capture rules ------------------------------------------------------------------------------

    [Fact]
    public void A_corrected_json_path_survives()
    {
        // The single most valuable thing on the screen and the hardest to work out twice: this
        // provider nests the token, and someone found that out by reading a real response.
        var merged = TemplateSeedMerge.Captures(
            [Capture("oauth2_access_token", "$.data.access_token")],
            [Capture("oauth2_access_token", "$.access_token")]);

        Assert.Equal("$.data.access_token", merged.Single().Expression);
    }

    [Fact]
    public void A_capture_for_a_variable_the_template_does_not_know_is_kept()
    {
        var merged = TemplateSeedMerge.Captures(
            [Capture("tenant_id", "$.tenant")],
            [Capture("oauth2_access_token", "$.access_token")]);

        Assert.Contains(merged, c => c.VariableName == "tenant_id");
        Assert.Contains(merged, c => c.VariableName == "oauth2_access_token");
    }

    [Fact]
    public void Two_rules_never_end_up_writing_one_variable()
    {
        // A variable written by two rules is a conflict, not a pair - whichever ran last would win,
        // invisibly.
        var merged = TemplateSeedMerge.Captures(
            [Capture("oauth2_access_token", "$.data.access_token")],
            [Capture("oauth2_access_token", "$.access_token"), Capture("oauth2_refresh_token", "$.refresh_token")]);

        Assert.Single(merged, c => c.VariableName == "oauth2_access_token");
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void A_blank_capture_row_is_not_carried_over()
    {
        // An empty row is someone part-way through typing, not a rule.
        var merged = TemplateSeedMerge.Captures(
            [Capture("", "")],
            [Capture("oauth2_access_token", "$.access_token")]);

        Assert.Single(merged);
    }

    // ---- variable names ------------------------------------------------------------------------------

    [Fact]
    public void A_renamed_access_token_variable_survives()
    {
        // It is what the Authorization: Bearer header reads. Renaming it is deliberate.
        Assert.Equal("my_token", TemplateSeedMerge.Name("my_token", AuthDefaults.AccessTokenVariable));
    }

    [Fact]
    public void A_blank_name_takes_the_templates()
    {
        Assert.Equal(AuthDefaults.AccessTokenVariable, TemplateSeedMerge.Name("", AuthDefaults.AccessTokenVariable));
    }
}
