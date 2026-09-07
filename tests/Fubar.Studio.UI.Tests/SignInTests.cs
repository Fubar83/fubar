using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Signing a person in: what gets saved, what the browser step is told, and what is left behind
/// afterwards.
///
/// <para>The persistence half is the one that had been quietly broken. Everything the browser step
/// needs - the authorize URL, the provider, the pinned port, the extra parameters - lived only in the
/// view model, so a profile reopened with an empty authorize URL and an ephemeral port, having already
/// told the provider to expect a fixed one.</para>
/// </summary>
public class SignInTests
{
    private static TokenRequestEditorViewModel Editor() =>
        new(new NoFilePicker(), new NoSchemaValidator());

    private static SignInProvider Provider(string key) =>
        SignInProviderCatalog.ByKey(key) ?? throw new InvalidOperationException($"no provider {key}");

    /// <summary>
    /// The whole setup, as a user does it: choose the provider in the one list, press Apply.
    ///
    /// <para>This used to be four steps - pick a grant, apply it, pick a provider, apply that - which
    /// is what the single list replaced.</para>
    /// </summary>
    private static void SetUpAs(TokenRequestEditorViewModel editor, string providerKey)
    {
        editor.SelectedTemplate = TokenRequestEditorViewModel.TemplateOptions
            .Single(t => t.ProviderKey == providerKey);

        editor.ApplyTemplateCommand.Execute(null);
    }

    // ---- what survives a save -----------------------------------------------------------------------

    [Fact]
    public void The_whole_sign_in_setup_round_trips_through_a_config()
    {
        var editor = Editor();
        SetUpAs(editor, "google");
        editor.RedirectPort = 8765;

        var config = new AuthConfig();
        editor.ApplyTo(config);

        var reopened = Editor();
        reopened.LoadFrom(config);

        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", reopened.AuthorizeUrl);
        Assert.Equal(8765, reopened.RedirectPort);
        Assert.Equal("google", reopened.SelectedProvider?.Key);
        Assert.Equal("offline", reopened.AuthorizeParameters.Rows.Single(r => r.Key == "access_type").Value);
    }

    [Fact]
    public void A_reopened_sign_in_still_shows_its_browser_step()
    {
        // The step is revealed by the template's grant, and the template picker starts on nothing when
        // a saved config is loaded. Without inferring it from the request, a working profile reopened
        // looking as though its sign-in had been lost.
        var editor = Editor();
        SetUpAs(editor, "microsoft");

        var config = new AuthConfig();
        editor.ApplyTo(config);

        var reopened = Editor();
        reopened.LoadFrom(config);

        Assert.True(reopened.IsAuthorizationCode);
    }

    [Fact]
    public void A_profile_naming_an_unknown_provider_still_loads()
    {
        // Written by a newer version, or naming one since removed. The URLs are what the sign-in
        // actually runs on, and they were saved as plain text - only the label is missing.
        var reopened = Editor();

        reopened.LoadFrom(new AuthConfig
        {
            AuthorizeUrl = "https://login.example.com/authorize",
            SignInProviderKey = "a-provider-from-the-future",
            RedirectPort = 9000,
        });

        Assert.Null(reopened.SelectedProvider);
        Assert.Equal("https://login.example.com/authorize", reopened.AuthorizeUrl);
        Assert.Equal(9000, reopened.RedirectPort);
    }

    [Fact]
    public void An_ephemeral_port_is_saved_as_absent_rather_than_as_zero()
    {
        // Absent is what a file written before this existed looks like, so the two have to mean the
        // same thing - otherwise the meaning of an old profile depends on which version wrote it.
        var config = new AuthConfig();
        Editor().ApplyTo(config);

        Assert.Null(config.RedirectPort);
    }

    // ---- the redirect URI shown on screen -----------------------------------------------------------

    [Fact]
    public void The_redirect_uri_follows_the_pinned_port_immediately()
    {
        // Shown before the first attempt, because it must be registered before the first attempt.
        var editor = Editor();

        editor.RedirectPort = 8765;

        Assert.Equal("http://127.0.0.1:8765/callback", editor.RedirectUri);
    }

    [Fact]
    public void Pinning_and_unpinning_moves_between_a_real_port_and_none()
    {
        var editor = Editor();

        Assert.False(editor.IsRedirectPortPinned);

        editor.IsRedirectPortPinned = true;
        Assert.Equal(TokenRequestEditorViewModel.DefaultPinnedRedirectPort, editor.RedirectPort);

        editor.IsRedirectPortPinned = false;
        Assert.Equal(0, editor.RedirectPort);
    }

    [Fact]
    public void Setting_a_provider_up_leaves_the_template_picker_on_a_real_option()
    {
        // A ComboBox shows its placeholder for a selection that is not one of its own items, and a
        // provider template is built on the fly rather than taken from the catalog. So "Set up" filled
        // the whole screen in correctly and blanked the template box above it, which reads as a failure.
        var editor = Editor();
        SetUpAs(editor, "google");

        Assert.Contains(editor.SelectedTemplate, TokenRequestEditorViewModel.TemplateOptions);
        Assert.True(editor.IsAuthorizationCode);
    }

    // ---- applying a template over work already done ---------------------------------------------------

    [Fact]
    public void Applying_a_provider_keeps_a_client_id_already_entered()
    {
        // The complaint this whole merge exists for. Applying used to replace the body outright, so
        // filling in your client id and then pressing Apply again - to fix a tenant, to try another
        // provider - silently threw it away.
        var editor = Editor();
        SetUpAs(editor, "google");

        editor.Body.UrlEncoded.Rows.Single(r => r.Key == "client_id").Value = "123-abc.apps.googleusercontent.com";

        SetUpAs(editor, "google");

        Assert.Equal(
            "123-abc.apps.googleusercontent.com",
            editor.Body.UrlEncoded.Rows.Single(r => r.Key == "client_id").Value);
    }

    [Fact]
    public void Switching_provider_keeps_what_only_the_user_knows()
    {
        // Realising halfway through that it is Entra, not Google. The endpoints must change; the
        // client id and the header you added must not.
        var editor = Editor();
        SetUpAs(editor, "google");

        editor.Body.UrlEncoded.Rows.Single(r => r.Key == "client_id").Value = "mine";
        editor.Headers.AddRowQuietly(KeyValueRowViewModel.FromModel(
            new KeyValueItem { Key = "X-Gateway", Value = "internal" }));

        SetUpAs(editor, "microsoft");

        Assert.Equal("mine", editor.Body.UrlEncoded.Rows.Single(r => r.Key == "client_id").Value);
        Assert.Equal("internal", editor.Headers.Rows.Single(r => r.Key == "X-Gateway").Value);
        Assert.Contains("login.microsoftonline.com", editor.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Applying_after_discover_keeps_the_discovered_endpoints()
    {
        // Discover is the recommended way in, and it was the worst thing to do first: everything it
        // found went the moment a template was applied.
        var editor = Editor();
        editor.Url = "https://login.example.com/oauth/token";
        editor.AuthorizeUrl = "https://login.example.com/oauth/authorize";

        editor.SelectedTemplate = TokenRequestEditorViewModel.TemplateOptions
            .Single(t => t.ProviderKey == "custom");
        editor.ApplyTemplateCommand.Execute(null);

        Assert.Equal("https://login.example.com/oauth/token", editor.Url);
        Assert.Equal("https://login.example.com/oauth/authorize", editor.AuthorizeUrl);
    }

    [Fact]
    public void A_corrected_capture_survives_a_reapply()
    {
        var editor = Editor();
        SetUpAs(editor, "google");

        editor.Captures.Single(c => c.VariableName == AuthDefaults.AccessTokenVariable).Expression = "$.data.access_token";

        SetUpAs(editor, "google");

        Assert.Equal(
            "$.data.access_token",
            editor.Captures.Single(c => c.VariableName == AuthDefaults.AccessTokenVariable).Expression);
    }

    [Fact]
    public void Changing_the_grant_still_changes_the_grant()
    {
        // The merge must not be so protective that applying a template stops doing its job.
        var editor = Editor();
        SetUpAs(editor, "google");

        editor.SelectedTemplate = TokenRequestEditorViewModel.TemplateOptions
            .Single(t => t.Key == "oauth2-client-credentials");
        editor.ApplyTemplateCommand.Execute(null);

        Assert.Equal("client_credentials", editor.Body.UrlEncoded.Rows.Single(r => r.Key == "grant_type").Value);
    }

    [Fact]
    public void Applying_with_a_tenant_typed_in_uses_that_tenant()
    {
        // The listed options were built with each provider's default tenant. Applying the listed copy
        // rather than rebuilding for the current one would quietly set Entra back to /common.
        var editor = Editor();
        editor.SelectedTemplate = TokenRequestEditorViewModel.TemplateOptions
            .Single(t => t.ProviderKey == "microsoft");
        editor.Tenant = "contoso.onmicrosoft.com";

        editor.ApplyTemplateCommand.Execute(null);

        Assert.Contains("contoso.onmicrosoft.com", editor.Url, StringComparison.Ordinal);
        Assert.Contains("contoso.onmicrosoft.com", editor.AuthorizeUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Applying_says_that_what_was_entered_was_kept()
    {
        // The merge is invisible otherwise, and someone burned once by a template wiping their client
        // id will not press the button again to find out it now behaves.
        var editor = Editor();
        SetUpAs(editor, "google");

        Assert.Contains("Google", editor.ApplyStatus!, StringComparison.Ordinal);
        Assert.Contains("kept", editor.ApplyStatus!, StringComparison.Ordinal);
    }

    [Fact]
    public void Signing_in_with_a_provider_is_one_choice_not_two()
    {
        // It took four interactions: pick a grant, apply it, pick a provider, apply that - and the
        // first two required knowing that Google's sign-in IS an authorization-code grant.
        Assert.Contains(
            TokenRequestEditorViewModel.TemplateOptions,
            t => t.ProviderKey == "google" && t.Grant == OAuth2GrantType.AuthorizationCode);

        // And no nameless generic entry beside them asking to be told apart.
        Assert.DoesNotContain(
            TokenRequestEditorViewModel.TemplateOptions,
            t => t.Grant == OAuth2GrantType.AuthorizationCode && t.ProviderKey is null);
    }

    // ---- what the browser step is told --------------------------------------------------------------

    [Fact]
    public async Task Signing_in_passes_the_pinned_port_and_the_extra_parameters()
    {
        var editor = Editor();
        SetUpAs(editor, "google");
        editor.RedirectPort = 8765;

        SignInRequest? seen = null;
        editor.SignInHandler = request =>
        {
            seen = request;

            return Task.FromResult(new SignInResult(true, "ok", "http://127.0.0.1:8765/callback"));
        };

        await editor.SignInCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.Equal(8765, seen.RedirectPort);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", seen.AuthorizeUrl);
        Assert.Contains(seen.ExtraParameters, p => p is { Key: "access_type", Value: "offline" });

        // The scopes as the token request carries them, so the browser step and the exchange cannot
        // disagree about what was asked for.
        Assert.Equal("openid email profile", seen.Scopes);
    }

    [Fact]
    public async Task Signing_in_without_an_authorize_url_says_so_rather_than_opening_a_browser()
    {
        var editor = Editor();
        var called = false;
        editor.SignInHandler = _ =>
        {
            called = true;

            return Task.FromResult(new SignInResult(true, "ok", null));
        };

        await editor.SignInCommand.ExecuteAsync(null);

        Assert.False(called);
        Assert.Contains("authorize URL", editor.SignInStatus!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_authorize_parameter_is_not_sent()
    {
        // The tick box is the only way to try a sign-in without a parameter you suspect. One that did
        // nothing would make that diagnosis impossible.
        var request = new SignInRequest(
            "https://login.example.com/authorize",
            "id",
            null,
            [
                new KeyValueItem { Key = "prompt", Value = "consent", Enabled = false },
                new KeyValueItem { Key = "audience", Value = "api", Enabled = true },
            ]);

        Assert.Equal("audience", Assert.Single(request.ExtraParameters).Key);
    }

    // ---- the session variables the exchange reads ---------------------------------------------------

    [Fact]
    public async Task A_successful_sign_in_leaves_the_code_and_verifier_for_the_exchange()
    {
        var session = new FakeSession();
        var service = new SignInService(new FakeListener(new AuthorizationCallback("the-code", null, null)), session);

        var result = await service.SignInAsync(
            new SignInRequest("https://login.example.com/authorize", "id", "openid", RedirectPort: 8765),
            Ws,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal("the-code", session.Get(Scope, SignInService.CodeVariable));
        Assert.False(string.IsNullOrEmpty(session.Get(Scope, SignInService.VerifierVariable)));
        Assert.Equal("http://127.0.0.1:8765/callback", session.Get(Scope, SignInService.RedirectUriVariable));
    }

    [Fact]
    public async Task A_pinned_port_is_used_as_given_rather_than_asked_for()
    {
        var listener = new FakeListener(new AuthorizationCallback("c", null, null));
        var service = new SignInService(listener, new FakeSession());

        await service.SignInAsync(
            new SignInRequest("https://login.example.com/authorize", "id", null, RedirectPort: 8765),
            Ws,
            null,
            TestContext.Current.CancellationToken);

        Assert.False(listener.PortWasReserved);
        Assert.Equal("http://127.0.0.1:8765/callback", listener.Seen!.RedirectUri);
    }

    [Fact]
    public async Task An_abandoned_sign_in_leaves_no_code_behind_for_the_next_one()
    {
        // An authorization code is single-use and short-lived. One left over from an attempt nobody
        // finished fails the next exchange with an error about the code, which says nothing about the
        // sign-in that was abandoned.
        var session = new FakeSession();
        session.Set(Scope, SignInService.CodeVariable, "stale");
        session.Set(Scope, SignInService.VerifierVariable, "stale");

        var service = new SignInService(
            new FakeListener(new AuthorizationCallback(null, "cancelled", "The sign-in was cancelled.")),
            session);

        var result = await service.SignInAsync(
            new SignInRequest("https://login.example.com/authorize", "id", null),
            Ws,
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Null(session.Get(Scope, SignInService.CodeVariable));
        Assert.Null(session.Get(Scope, SignInService.VerifierVariable));
    }

    [Fact]
    public async Task The_redirect_uri_is_stored_before_the_browser_opens()
    {
        // So it is there to copy into the provider's registration even when this attempt fails for
        // that exact reason - and so a redirect arriving while the user is still deciding has
        // somewhere to land.
        var session = new FakeSession();
        var listener = new FakeListener(new AuthorizationCallback(null, "port_unavailable", "nope"))
        {
            OnListen = () => Assert.Equal(
                "http://127.0.0.1:8765/callback",
                session.Get(Scope, SignInService.RedirectUriVariable)),
        };

        await new SignInService(listener, session).SignInAsync(
            new SignInRequest("https://login.example.com/authorize", "id", null, RedirectPort: 8765),
            Ws,
            null,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_empty_authorize_url_is_refused_before_a_port_is_taken()
    {
        var listener = new FakeListener(new AuthorizationCallback("c", null, null));

        var result = await new SignInService(listener, new FakeSession())
            .SignInAsync(new SignInRequest("", "id", null, RedirectPort: 8765), Ws, null, TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Null(listener.Seen);
    }

    // ---- fixtures -----------------------------------------------------------------------------------

    private static readonly Workspace Ws =
        new() { RootPath = "root", Manifest = new AppManifest { Name = "t" } };

    private static string Scope => SessionScope.For(Ws, (WorkspaceEnvironment?)null);

    private sealed class FakeListener(AuthorizationCallback callback) : IAuthorizationCodeListener
    {
        public bool PortWasReserved { get; private set; }

        public AuthorizationRequest? Seen { get; private set; }

        /// <summary>Runs at the moment the browser would open, to assert what is already in place.</summary>
        public Action? OnListen { get; init; }

        public int ReservePort()
        {
            PortWasReserved = true;

            return 55555;
        }

        public Task<AuthorizationCallback> ListenAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Seen = request;
            OnListen?.Invoke();

            return Task.FromResult(callback);
        }
    }

    private sealed class FakeSession : ISessionVariableStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Get(string scope, string key) =>
            _values.TryGetValue($"{scope}/{key}", out var value) ? value : null;

        public void Set(string scope, string key, string? value)
        {
            if (value is null)
            {
                _values.Remove($"{scope}/{key}");

                return;
            }

            _values[$"{scope}/{key}"] = value;
        }

        public bool TryGet(string scope, string key, out string value)
        {
            value = Get(scope, key) ?? "";

            return value.Length > 0;
        }

        public IReadOnlyDictionary<string, string> Snapshot(string scope) => new Dictionary<string, string>();

        public void Clear(string scope) => _values.Clear();
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
