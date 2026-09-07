using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Turning a token response into capture rules - the step that used to be pure guesswork, and the one
/// whose button nobody could explain.
///
/// <para><c>TokenResponseFields</c> (reading the payload) was tested; the wiring from a listed field to
/// a working rule was not, which is the half a user actually presses.</para>
/// </summary>
public class CaptureFromResponseTests
{
    private const string Typical =
        """{"access_token":"eyJhbGciOi.abc.def","refresh_token":"r-123","expires_in":3600,"token_type":"Bearer"}""";

    private static TokenRequestEditorViewModel Editor(string body = Typical)
    {
        var editor = new TokenRequestEditorViewModel(new NoFilePicker(), new NoSchemaValidator());

        // The path a real Test / Get token takes: the provider answered, and these are its fields.
        editor.TestAuthHandler = _ => Task.FromResult(
            new AuthOutcome(true, "ok") { Response = new TokenResponse(200, body) });

        return editor;
    }

    private static async Task<TokenRequestEditorViewModel> AfterTestAsync(string body = Typical)
    {
        var editor = Editor(body);
        await editor.TestAuthCommand.ExecuteAsync(null);

        return editor;
    }

    [Fact]
    public async Task A_token_response_lists_the_fields_it_actually_returned()
    {
        var editor = await AfterTestAsync();

        Assert.Equal("HTTP 200", editor.ResponseStatus);
        Assert.True(editor.HasResponse);
        Assert.Contains(editor.CapturableFields, f => f.Path == "$.access_token");
        Assert.Contains(editor.CapturableFields, f => f.Path == "$.refresh_token");
    }

    [Fact]
    public async Task The_button_names_the_variable_the_value_lands_in()
    {
        // "Capture" was this codebase's word for it and said nothing about where the value goes. The
        // label now matches the Bearer line at the top of the same screen.
        var editor = await AfterTestAsync();

        var token = editor.CapturableFields.Single(f => f.Path == "$.access_token");

        Assert.Equal($"Save as {{{{{AuthDefaults.AccessTokenVariable}}}}}", token.ActionLabel);
    }

    [Fact]
    public async Task Saving_the_access_token_writes_the_variable_the_bearer_header_reads()
    {
        // The commonest case wired up by one click rather than by knowing the convention: the rule's
        // variable has to be the one AppliesAs advertises, or the token is captured and never sent.
        var editor = await AfterTestAsync();

        editor.CaptureFieldCommand.Execute(editor.CapturableFields.Single(f => f.Path == "$.access_token"));

        var rule = editor.Captures.Single(c => c.Expression == "$.access_token");

        Assert.Equal(AuthDefaults.AccessTokenVariable, rule.VariableName);
        Assert.Contains($"{{{{{rule.VariableName}}}}}", editor.AppliesAs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_saved_field_says_so_instead_of_offering_the_button_again()
    {
        // It used to offer the button either way and silently do nothing on the second click, which
        // reads as a broken button rather than as "already done".
        var editor = await AfterTestAsync();

        editor.CaptureFieldCommand.Execute(editor.CapturableFields.Single(f => f.Path == "$.access_token"));

        var token = editor.CapturableFields.Single(f => f.Path == "$.access_token");

        Assert.True(token.IsCaptured);
        Assert.Contains(AuthDefaults.AccessTokenVariable, token.CapturedLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_the_same_field_twice_does_not_make_two_rules()
    {
        // Two rules writing one variable is a conflict, not a pair - whichever ran last would win,
        // invisibly. Clicking twice is something people do.
        var editor = await AfterTestAsync();
        var field = editor.CapturableFields.Single(f => f.Path == "$.access_token");

        editor.CaptureFieldCommand.Execute(field);
        editor.CaptureFieldCommand.Execute(field);

        Assert.Single(editor.Captures, c => c.Expression == "$.access_token");
    }

    [Fact]
    public async Task Everything_saved_is_session_scoped()
    {
        // The environment scope persists to a committed file, and everything a token endpoint returns
        // is credential-shaped.
        var editor = await AfterTestAsync();

        foreach (var field in editor.CapturableFields.ToList())
        {
            editor.CaptureFieldCommand.Execute(field);
        }

        Assert.NotEmpty(editor.Captures);
        Assert.All(editor.Captures, c => Assert.Equal(CaptureScope.Session, c.Scope));
    }

    [Fact]
    public async Task A_saved_rule_survives_into_the_config_the_provider_runs()
    {
        // The round trip that matters: what the button creates has to be what the auth provider later
        // applies, or the whole screen is a drawing of a capture rule.
        var editor = await AfterTestAsync();

        editor.CaptureFieldCommand.Execute(editor.CapturableFields.Single(f => f.Path == "$.refresh_token"));

        var config = new AuthConfig();
        editor.ApplyTo(config);

        var rule = config.TokenCaptures.Single(c => c.Expression == "$.refresh_token");

        Assert.Equal("refresh_token", rule.VariableName);
        Assert.Equal(ResponseField.JsonBody, rule.Source);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public async Task A_response_that_is_not_json_shows_its_body_rather_than_inventing_paths()
    {
        // Form-encoded and HTML both happen - a proxy sign-in page, a provider that needs an Accept
        // header. Offering made-up paths into those would be worse than offering none.
        var editor = await AfterTestAsync("access_token=abc&token_type=bearer");

        Assert.Empty(editor.CapturableFields);
        Assert.False(editor.HasResponseFields);
        Assert.Equal("access_token=abc&token_type=bearer", editor.ResponseBody);
    }

    [Fact]
    public async Task A_failing_token_request_still_shows_what_came_back()
    {
        // Especially on failure: this is the body that says invalid_client, and why.
        var editor = new TokenRequestEditorViewModel(new NoFilePicker(), new NoSchemaValidator())
        {
            TestAuthHandler = _ => Task.FromResult(
                new AuthOutcome(false, "Token request failed")
                {
                    Response = new TokenResponse(400, """{"error":"invalid_client"}"""),
                }),
        };

        await editor.TestAuthCommand.ExecuteAsync(null);

        Assert.Equal("HTTP 400", editor.ResponseStatus);
        Assert.Contains("invalid_client", editor.ResponseBody, StringComparison.Ordinal);
    }

    private sealed class NoFilePicker : IFilePickerService
    {
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenFileAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class NoSchemaValidator : IJsonSchemaValidator
    {
        public IReadOnlyList<string> Validate(string schemaJson, string bodyJson) => [];
    }
}
