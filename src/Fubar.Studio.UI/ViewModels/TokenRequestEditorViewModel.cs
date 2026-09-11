using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.UI.Controls;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// The request-builder-style OAuth2 editor shared by the per-request Auth tab
/// (<see cref="RequestAuthViewModel"/>) and the standalone <see cref="AuthProfileEditorViewModel"/>. Edits
/// the token/login request as a real request (method/URL + <see cref="Headers"/> + <see cref="Body"/>),
/// seeded from an <see cref="AuthTemplate"/>, plus the <see cref="Captures"/> (JSONPath → session variable)
/// that pull tokens out of the response. Round-trips the OAuth2 half of an <see cref="AuthConfig"/> via
/// <see cref="LoadFrom"/>/<see cref="ApplyTo"/>; a legacy fixed-form config is upgraded on load through
/// <see cref="OAuth2LegacyTemplate"/>.
/// </summary>
public partial class TokenRequestEditorViewModel : ViewModelBase
{
    public TokenRequestEditorViewModel(IFilePickerService filePickerService, IJsonSchemaValidator schemaValidator)
    {
        Headers = new KeyValueGridViewModel();
        Body = new RequestBodyViewModel(filePickerService, schemaValidator);
        AuthorizeParameters = new KeyValueGridViewModel();

        Headers.Changed += RaiseChanged;
        Body.Changed += RaiseChanged;
        Body.PropertyChanged += (_, _) => RaiseChanged();
        AuthorizeParameters.Changed += RaiseChanged;
        Captures.CollectionChanged += (_, _) => RaiseChanged();
    }

    /// <summary>
    /// Everything this editor can be set up as, in one list.
    ///
    /// <para>The providers used to live in a SECOND picker, inside the sign-in box that only appeared
    /// once the authorization-code template had already been applied. So "sign in with Google" was
    /// four interactions - pick a grant, apply it, pick a provider, apply that - and the first two
    /// required knowing that Google's sign-in IS an authorization-code grant, which is exactly the
    /// knowledge the presets exist to not require. One list, one Apply.</para>
    ///
    /// <para>Cached, not rebuilt per call: these are records holding lists, so two separately
    /// constructed copies are not equal, and a ComboBox whose SelectedItem is not one of its own items
    /// shows its placeholder instead.</para>
    /// </summary>
    public static IReadOnlyList<AuthTemplate> TemplateOptions { get; } =
    [
        .. SignInProviderCatalog.All.Select(p => SignInProviderTemplate.For(p)),

        // The catalog's own authorization-code entry is deliberately left out: every sign-in entry
        // above IS one, and offering a nameless fifth would ask the user to tell them apart. It stays
        // in the catalog because a profile saved before this reopens on it - Seed falls back to
        // matching on Grant, which lands on the first sign-in entry.
        .. AuthTemplateCatalog.All.Where(t => t.Grant != OAuth2GrantType.AuthorizationCode),
    ];

    public static IReadOnlyList<string> MethodOptions { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public static IReadOnlyList<ResponseField> SourceOptions { get; } = Enum.GetValues<ResponseField>();

    /// <summary>Raised on any edit so an owner can mark itself dirty / recompute derived headers.</summary>
    public event Action? Changed;

    private void RaiseChanged()
    {
        RefreshGuidance();
        Changed?.Invoke();
    }

    /// <summary>
    /// What a request using this profile will actually send, stated permanently rather than only
    /// inside the Verify preview.
    ///
    /// This is the single most clarifying sentence in the whole feature - it is the link between the
    /// token request being edited and the requests it exists to serve - and it used to be behind a
    /// button. Someone who has not pressed that button has no way to know the captured variable is
    /// what the Bearer header reads, which makes the captures grid look like a set of unrelated
    /// scratch values.
    /// </summary>
    public string AppliesAs
    {
        get
        {
            var variable = string.IsNullOrWhiteSpace(AccessTokenVariable)
                ? AuthDefaults.AccessTokenVariable
                : AccessTokenVariable;

            return $"Requests using this profile send:  Authorization: Bearer {{{{{variable}}}}}";
        }
    }

    /// <summary>
    /// The variables this token request reads, each marked defined or not - see
    /// <see cref="TokenRequestVariables"/>.
    ///
    /// The per-field tooltip already tints one box at a time, which answers for the box under the
    /// pointer. The variable nobody defined is usually in a field they are not looking at, which is
    /// what this is for.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TokenRequestVariable> RequiredVariables { get; private set; } = [];

    /// <summary>A one-line summary of the above, or null when the request reads no variables.</summary>
    [ObservableProperty]
    public partial string? VariableSummary { get; private set; }

    /// <summary>True when something the request needs is undefined, so the line can be drawn as a warning.</summary>
    public bool HasMissingVariables => RequiredVariables.Any(v => !v.IsResolved);

    partial void OnRequiredVariablesChanged(IReadOnlyList<TokenRequestVariable> value) =>
        OnPropertyChanged(nameof(HasMissingVariables));

    private void RefreshGuidance()
    {
        OnPropertyChanged(nameof(AppliesAs));

        // No resolver means no host has given this editor a variable context - the Gallery, a test.
        // Reporting everything as undefined there would be worse than saying nothing.
        if (VariableContext is not { } context)
        {
            RequiredVariables = [];
            VariableSummary = null;

            return;
        }

        var request = new AuthTokenRequest
        {
            Method = Method,
            Url = Url,
            Headers = Headers.ToModel(),
            Body = Body.ToModel(),
        };

        RequiredVariables = TokenRequestVariables.Of(
            request,
            text => context.Resolver.Substitute(text, context.Workspace, context.ActiveEnvironment) ?? text);

        VariableSummary = TokenRequestVariables.Describe(RequiredVariables);
    }

    [ObservableProperty]
    public partial AuthTemplate? SelectedTemplate { get; set; }

    [ObservableProperty]
    public partial string Method { get; set; } = "POST";

    [ObservableProperty]
    public partial string Url { get; set; } = "";

    public KeyValueGridViewModel Headers { get; }

    public RequestBodyViewModel Body { get; }

    /// <summary>
    /// Extra query parameters for the AUTHORIZE URL - not for the token request, which has its own
    /// headers and body above.
    ///
    /// <para>A grid rather than a hidden concern because the parameters that belong here are the ones
    /// that decide whether a sign-in is usable at all: Google returns a refresh token only with
    /// <c>access_type=offline</c>, Auth0 returns an opaque string instead of a JWT without
    /// <c>audience</c>. Both fail long after the sign-in appeared to succeed.</para>
    /// </summary>
    public KeyValueGridViewModel AuthorizeParameters { get; }

    public ObservableCollection<CaptureRowViewModel> Captures { get; } = [];

    [ObservableProperty]
    public partial string AccessTokenVariable { get; set; } = "";

    [ObservableProperty]
    public partial string ExpiryVariable { get; set; } = "";

    [ObservableProperty]
    public partial string ExpiresInExpression { get; set; } = "";

    /// <summary>Variable tooltip/intellisense context for the URL and field editors. Set by the owner
    /// (which knows the workspace/active environment); null disables the {{variable}} affordances.</summary>
    [ObservableProperty]
    public partial VariableTooltipContext? VariableContext { get; set; }

    // The context arrives after construction and can be replaced when the active environment changes,
    // and it is what decides whether a variable counts as defined - so the guidance has to be
    // recomputed when it does, not only when the request text is edited.
    partial void OnVariableContextChanged(VariableTooltipContext? value) => RefreshGuidance();

    /// <summary>Set by the owner so Test can acquire a token via the <c>IAuthProvider</c>.</summary>
    public Func<AuthConfig, Task<AuthOutcome>>? TestAuthHandler { get; set; }

    /// <summary>Set by the owner so Verify can preview the token request without sending it.</summary>
    public Func<AuthConfig, string>? PreviewHandler { get; set; }

    [ObservableProperty]
    public partial string? TestStatus { get; set; }

    [ObservableProperty]
    public partial string? RequestPreview { get; set; }

    partial void OnMethodChanged(string value) => RaiseChanged();

    partial void OnUrlChanged(string value) => RaiseChanged();

    partial void OnAccessTokenVariableChanged(string value) => RaiseChanged();

    partial void OnExpiryVariableChanged(string value) => RaiseChanged();

    partial void OnExpiresInExpressionChanged(string value) => RaiseChanged();

    /// <summary>
    /// Fills the editor in from the chosen template, keeping what the user already supplied.
    ///
    /// <para>Also the provider path: a sign-in entry carries a <c>ProviderKey</c>, so choosing "Sign
    /// in with Google" here does everything the separate provider picker and its second Apply button
    /// used to do.</para>
    /// </summary>
    [RelayCommand]
    private void ApplyTemplate()
    {
        if (SelectedTemplate is not { } selected)
        {
            return;
        }

        // Rebuilt for the CURRENT tenant rather than used as listed: the cached options were built
        // with each provider's default tenant, so applying the listed copy after typing a tenant id
        // would quietly set Entra's URLs back to /common.
        var provider = SignInProviderCatalog.ByKey(selected.ProviderKey);
        var template = provider is null ? selected : SignInProviderTemplate.For(provider, Tenant);

        Seed(template);

        if (provider is not null)
        {
            AddScope(SignInProviderTemplate.ScopeValue(provider));

            // Filled in but NOT fetched. Discover is a network call to someone else's service, and
            // making a dropdown reach the internet is not a thing a dropdown should do.
            Issuer = TemplateSeedMerge.Url(Issuer, provider.IssuerFor(Tenant) ?? "");
        }

        ApplyStatus = DescribeApply(provider);
        RaiseChanged();
    }

    /// <summary>
    /// What applying just did, and - the part that matters - what it did not touch.
    ///
    /// <para>The merge is invisible if nobody says it happened: someone who has been burned once by a
    /// template wiping their client id will not press the button again to find out it now behaves.
    /// </para>
    /// </summary>
    [ObservableProperty]
    public partial string? ApplyStatus { get; private set; }

    private string DescribeApply(SignInProvider? provider)
    {
        var what = provider is null
            ? "Filled in this grant's request."
            : $"Filled in {provider.DisplayName}'s endpoints, scopes and parameters.";

        return $"{what} Anything you had already entered was kept.";
    }

    [RelayCommand]
    private void AddCapture()
    {
        var row = new CaptureRowViewModel(new CaptureRule { Scope = CaptureScope.Session });
        row.PropertyChanged += (_, _) => RaiseChanged();
        Captures.Add(row);
    }

    [RelayCommand]
    private void RemoveCapture(CaptureRowViewModel? row)
    {
        if (row is not null)
        {
            Captures.Remove(row);
        }
    }

    [RelayCommand]
    private async Task TestAuthAsync()
    {
        if (TestAuthHandler is null)
        {
            return;
        }

        TestStatus = "Requesting token...";
        var outcome = await TestAuthHandler(ToAuthConfig());
        TestStatus = outcome.Message;

        ShowResponse(outcome.Response);
    }

    /// <summary>
    /// What the token endpoint replied, and the paths you could capture from it.
    ///
    /// The step this exists for is the one that used to be pure guesswork: a capture rule is a
    /// JSONPath into this response, and the response was never shown. People guessed at field names,
    /// and a wrong guess fails identically to a wrong endpoint, a wrong secret or a wrong grant.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TokenResponseField> ResponseFields { get; private set; } = [];

    /// <summary>The status line of the last token response, e.g. <c>HTTP 400</c>. Empty until one arrives.</summary>
    [ObservableProperty]
    public partial string ResponseStatus { get; private set; } = "";

    /// <summary>The raw body, for the cases the field list cannot help with - HTML, XML, form-encoded.</summary>
    [ObservableProperty]
    public partial string ResponseBody { get; private set; } = "";

    public bool HasResponse => ResponseStatus.Length > 0;

    public bool HasResponseFields => ResponseFields.Count > 0;

    partial void OnResponseStatusChanged(string value) => OnPropertyChanged(nameof(HasResponse));

    partial void OnResponseFieldsChanged(IReadOnlyList<TokenResponseField> value)
    {
        OnPropertyChanged(nameof(HasResponseFields));
        RefreshCapturableFields();
    }

    private void ShowResponse(TokenResponse? response)
    {
        ResponseStatus = response is null ? "" : $"HTTP {response.StatusCode}";
        ResponseBody = response?.Body ?? "";
        ResponseFields = response?.Fields ?? [];
    }

    /// <summary>
    /// Turns a field of the response into a capture rule, naming the variable after the field.
    ///
    /// The whole point: the path is taken from a response that actually arrived, so it cannot be a
    /// typo or a guess at what the provider calls things. An existing rule for the same path is left
    /// alone rather than duplicated - clicking twice is something people do.
    /// </summary>
    [RelayCommand]
    private void CaptureField(CapturableField? field)
    {
        if (field is null || Captures.Any(c => string.Equals(c.Expression, field.Path, StringComparison.Ordinal)))
        {
            return;
        }

        var row = new CaptureRowViewModel(new CaptureRule
        {
            Enabled = true,
            VariableName = field.Variable,
            Source = ResponseField.JsonBody,
            Expression = field.Path,
            Scope = CaptureScope.Session,
        });

        row.PropertyChanged += (_, _) => RaiseChanged();
        Captures.Add(row);
        RefreshCapturableFields();
        RaiseChanged();
    }

    /// <summary>
    /// The response's fields as the screen offers them: each with the variable it would be saved into
    /// and whether that has already been done.
    ///
    /// <para>The button used to say "Capture", which is this codebase's word rather than anyone
    /// else's - it does not say what happens, where the value goes, or what to do with it afterwards.
    /// It says "Save as {{name}}" now, naming the exact variable, so the connection to the
    /// <c>Authorization: Bearer {{…}}</c> line at the top of the screen is on the button itself. A
    /// field already captured says so instead of offering a button that silently does nothing on the
    /// second click.</para>
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<CapturableField> CapturableFields { get; private set; } = [];

    private void RefreshCapturableFields() =>
        CapturableFields =
        [
            .. ResponseFields.Select(field =>
            {
                var leaf = field.Path[(field.Path.LastIndexOf('.') + 1)..];

                // The access token gets the variable the Bearer header already reads, so the commonest
                // case is wired up correctly by one click rather than by knowing that convention.
                var variable = leaf is "access_token" or "id_token"
                    ? (string.IsNullOrWhiteSpace(AccessTokenVariable) ? AuthDefaults.AccessTokenVariable : AccessTokenVariable)
                    : leaf;

                var captured = Captures.FirstOrDefault(c =>
                    string.Equals(c.Expression, field.Path, StringComparison.Ordinal));

                return new CapturableField(
                    field.Path,
                    field.Preview,
                    captured?.VariableName ?? variable,
                    captured is not null);
            }),
        ];

    [RelayCommand]
    private void VerifyRequest() => RequestPreview = PreviewHandler?.Invoke(ToAuthConfig());

    /// <summary>
    /// Fetches the provider's OpenID configuration and fills in what it says. Set by the host, which
    /// owns the HTTP client; null leaves the Discover button inert.
    /// </summary>
    public Func<string, Task<DiscoveryResult>>? DiscoveryHandler { get; set; }

    // ---- Signing in (authorization code + PKCE) --------------------------------------------------

    /// <summary>
    /// Runs the browser half of the authorization-code grant and returns what came back. Set by the
    /// host, which owns the browser and the socket.
    /// </summary>
    public Func<SignInRequest, Task<SignInResult>>? SignInHandler { get; set; }

    // ---- Which provider ---------------------------------------------------------------------------
    //
    // Derived from the selected template rather than picked separately. There used to be a second
    // ComboBox and a second Apply button here, inside a box that only appeared once the
    // authorization-code template had already been applied - so choosing Google meant four
    // interactions, the first two of which required knowing that Google's sign-in IS an
    // authorization-code grant. One list answers both questions now.

    /// <summary>The provider the selected template was built from, or null for the plain grants.</summary>
    public SignInProvider? SelectedProvider => SignInProviderCatalog.ByKey(SelectedTemplate?.ProviderKey);

    /// <summary>The tenant, directory or domain, for the providers that need one.</summary>
    [ObservableProperty]
    public partial string Tenant { get; set; } = "";

    public bool NeedsTenant => SelectedProvider?.NeedsTenant == true;

    public string TenantLabel => SelectedProvider?.TenantLabel ?? "";

    public string TenantHelp => SelectedProvider?.TenantHelp ?? "";

    /// <summary>What to do in the provider's own console before any of this can work.</summary>
    public string SetupSummary => SelectedProvider?.SetupSummary ?? "";

    /// <summary>Anything true of this provider the user would otherwise learn the hard way.</summary>
    public string? ProviderCaveat => SelectedProvider?.Caveat;

    public bool HasProviderCaveat => !string.IsNullOrWhiteSpace(ProviderCaveat);

    public string? ConsoleUrl => SelectedProvider?.ConsoleUrl;

    public bool HasConsoleUrl => !string.IsNullOrWhiteSpace(ConsoleUrl);

    private void RefreshProvider()
    {
        // The tenant default comes with the provider, so choosing Microsoft does not leave an empty box
        // that silently builds a URL containing a literal {tenant}.
        if (SelectedProvider?.TenantDefault is { } tenant && string.IsNullOrWhiteSpace(Tenant))
        {
            Tenant = tenant;
        }

        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(NeedsTenant));
        OnPropertyChanged(nameof(TenantLabel));
        OnPropertyChanged(nameof(TenantHelp));
        OnPropertyChanged(nameof(SetupSummary));
        OnPropertyChanged(nameof(ProviderCaveat));
        OnPropertyChanged(nameof(HasProviderCaveat));
        OnPropertyChanged(nameof(ConsoleUrl));
        OnPropertyChanged(nameof(HasConsoleUrl));
    }


    /// <summary>The provider's authorize endpoint. Filled by Discover when the provider publishes one.</summary>
    [ObservableProperty]
    public partial string AuthorizeUrl { get; set; } = "";

    /// <summary>
    /// The redirect this app will listen on, shown BEFORE the flow runs rather than after it fails.
    ///
    /// It has to be registered with the provider exactly as written, and a sign-in that fails because
    /// it was not is the single most opaque failure in this grant - the browser shows the provider's
    /// own error page and the app never hears anything at all.
    ///
    /// <para>Derived from <see cref="RedirectPort"/> rather than reported by the listener, which is
    /// the only way it can be shown up front: an ephemeral port is not chosen until the socket binds,
    /// so with one the best that can be said is which part is not yet known.</para>
    /// </summary>
    public string RedirectUri => AuthorizationCodeFlow.RedirectUriFor(RedirectPort);

    /// <summary>
    /// The loopback port to catch the redirect on. Zero asks the OS for a free one.
    ///
    /// <para>Pinnable, and that is the point. It used to be ephemeral with no alternative, so the
    /// redirect URI carried a different port every attempt - while the editor told the user to
    /// register it with their provider, which was impossible for any provider that matches it
    /// exactly. Google and Entra ignore the port on loopback; GitHub, Okta, Auth0 and Keycloak do
    /// not.</para>
    /// </summary>
    [ObservableProperty]
    public partial int RedirectPort { get; set; }

    /// <summary>Bound to the tick box; unticking asks the OS for a port again.</summary>
    public bool IsRedirectPortPinned
    {
        get => RedirectPort > 0;
        set
        {
            if (value == IsRedirectPortPinned)
            {
                return;
            }

            // A number in the range providers are used to seeing in their own docs, and above the
            // privileged range so binding it never needs elevation.
            RedirectPort = value ? DefaultPinnedRedirectPort : AuthorizationCodeFlow.EphemeralPort;
        }
    }

    /// <summary>What pinning starts from. Arbitrary, and deliberately memorable.</summary>
    public const int DefaultPinnedRedirectPort = 8765;

    partial void OnRedirectPortChanged(int value)
    {
        OnPropertyChanged(nameof(RedirectUri));
        OnPropertyChanged(nameof(IsRedirectPortPinned));
        RaiseChanged();
    }

    /// <summary>True when the chosen template signs a person in, so the browser step is shown.</summary>
    public bool IsAuthorizationCode => SelectedTemplate?.Grant == OAuth2GrantType.AuthorizationCode;

    [ObservableProperty]
    public partial string? SignInStatus { get; private set; }

    /// <summary>
    /// Opens the browser, waits for the redirect, and puts the code and verifier where the token
    /// request can read them.
    ///
    /// Two steps rather than one because they genuinely are two: a person approving in a browser, then
    /// an ordinary HTTP request. Keeping the second half an editable request is what lets a provider
    /// with an extra required field be handled by adding one, instead of by waiting for this app to
    /// support it.
    /// </summary>
    [RelayCommand]
    private async Task SignInAsync()
    {
        if (SignInHandler is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AuthorizeUrl))
        {
            SignInStatus = "Set the authorize URL first, or press Discover to find it.";

            return;
        }

        SignInStatus = "Waiting for the browser…";

        var scopes = Body.UrlEncoded.Rows
            .FirstOrDefault(r => string.Equals(r.Key, "scope", StringComparison.OrdinalIgnoreCase))?.Value;

        var result = await SignInHandler(new SignInRequest(
            AuthorizeUrl,
            ClientIdInBody(),
            scopes,
            AuthorizeParameters.ToModel(),
            RedirectPort));

        // The URI the listener actually bound is worth saying when the port was ephemeral, because
        // then it is the only place that number appears - and it is what a provider rejecting the
        // redirect was rejecting.
        ActualRedirectUri = result.RedirectUri;
        SignInStatus = result.Message;
    }

    /// <summary>The redirect the last attempt really listened on. Null until one has run.</summary>
    [ObservableProperty]
    public partial string? ActualRedirectUri { get; private set; }

    /// <summary>
    /// True when the last attempt's redirect differs from the one on screen - which, with an ephemeral
    /// port, it always will. Shown so nobody registers a URI that was never going to come back.
    /// </summary>
    public bool ShowsActualRedirectUri =>
        ActualRedirectUri is { Length: > 0 } actual
        && !string.Equals(actual, RedirectUri, StringComparison.Ordinal);

    partial void OnActualRedirectUriChanged(string? value) => OnPropertyChanged(nameof(ShowsActualRedirectUri));

    /// <summary>
    /// The client id as the token request carries it, so the browser step and the exchange cannot
    /// disagree about who is asking - a mismatch there is rejected by the provider with an error about
    /// the code rather than about the client.
    /// </summary>
    private string ClientIdInBody() =>
        Body.UrlEncoded.Rows.FirstOrDefault(r => string.Equals(r.Key, "client_id", StringComparison.OrdinalIgnoreCase))?.Value
        ?? "";

    partial void OnSelectedTemplateChanged(AuthTemplate? value)
    {
        OnPropertyChanged(nameof(IsAuthorizationCode));
        RefreshProvider();
    }

    /// <summary>The issuer to look up. Usually pasted straight from the provider's own page.</summary>
    [ObservableProperty]
    public partial string Issuer { get; set; } = "";

    /// <summary>What discovery found, or why it did not.</summary>
    [ObservableProperty]
    public partial string? DiscoveryStatus { get; private set; }

    /// <summary>The scopes the provider advertises, offered rather than typed from memory.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> DiscoveredScopes { get; private set; } = [];

    public bool HasDiscoveredScopes => DiscoveredScopes.Count > 0;

    partial void OnDiscoveredScopesChanged(IReadOnlyList<string> value) =>
        OnPropertyChanged(nameof(HasDiscoveredScopes));

    /// <summary>
    /// Looks the provider up and fills the token URL in.
    ///
    /// This replaces "find the docs, find the right page, copy the endpoint, hope it is current" with
    /// pasting the issuer. Only the URL is written: the credentials are the user's and the body was
    /// seeded by the template, so overwriting either from a discovery document would throw away work
    /// to supply something it does not actually know.
    /// </summary>
    [RelayCommand]
    private async Task DiscoverAsync()
    {
        if (DiscoveryHandler is null)
        {
            return;
        }

        DiscoveryStatus = "Looking up the provider…";

        var result = await DiscoveryHandler(Issuer);

        DiscoveryStatus = result.Message;

        if (result.Configuration is not { } configuration)
        {
            DiscoveredScopes = [];

            return;
        }

        Url = configuration.TokenEndpoint ?? Url;
        AuthorizeUrl = configuration.AuthorizationEndpoint ?? AuthorizeUrl;
        DiscoveredScopes = configuration.ScopesSupported;
    }

    /// <summary>
    /// Adds a discovered scope to the token request's <c>scope</c> field, creating it if the template
    /// did not. Appended rather than replaced - scopes are a set, and choosing them one at a time is
    /// how anyone actually decides which they need.
    /// </summary>
    [RelayCommand]
    private void AddScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return;
        }

        var row = Body.UrlEncoded.Rows.FirstOrDefault(r => string.Equals(r.Key, "scope", StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            Body.UrlEncoded.AddRowQuietly(KeyValueRowViewModel.FromModel(new KeyValueItem { Key = "scope", Value = scope }));
            RaiseChanged();

            return;
        }

        var already = (row.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (!already.Contains(scope, StringComparer.Ordinal))
        {
            row.Value = string.Join(' ', already.Append(scope));
        }
    }

    /// <summary>Populate the editor from an <see cref="AuthConfig"/>'s OAuth2 fields: its template token
    /// request if present, otherwise the legacy fixed-form config (upgraded), otherwise the default template.</summary>
    public void LoadFrom(AuthConfig auth)
    {
        LoadTokenRequest(auth);

        // AFTER the branches above, never before: two of them go through Seed, which sets the
        // authorize URL and parameters from a template - so loading the saved sign-in first meant a
        // config with no token request had its authorize URL wiped by the default template's empty
        // one. The saved values are the authority here; a seed is only a starting point.
        LoadSignIn(auth);
    }

    private void LoadTokenRequest(AuthConfig auth)
    {
        if (auth.TokenRequest is { } tokenRequest)
        {
            Method = string.IsNullOrWhiteSpace(tokenRequest.Method) ? "POST" : tokenRequest.Method;
            Url = tokenRequest.Url;
            LoadHeaders(tokenRequest.Headers);
            LoadBody(tokenRequest.Body);
            LoadCaptures(auth.TokenCaptures);
            AccessTokenVariable = auth.AccessTokenVariable ?? "";
            ExpiryVariable = auth.ExpiryVariable ?? "";
            ExpiresInExpression = auth.ExpiresInExpression ?? "";

            // A saved token request that reads {{oauth2_code}} IS an authorization-code sign-in,
            // whatever template it came from. Without this the whole browser step is hidden on
            // reopening, because the template picker starts on nothing and IsAuthorizationCode is
            // false - so a working profile looked like it had lost its sign-in.
            if (SelectedTemplate is null && ReadsAuthorizationCode())
            {
                SelectedTemplate = AuthTemplateCatalog.All
                    .FirstOrDefault(t => t.Grant == OAuth2GrantType.AuthorizationCode);
            }

            return;
        }

        if (IsLegacyConfigured(auth))
        {
            var (request, captures) = OAuth2LegacyTemplate.FromLegacy(auth);
            Method = request.Method;
            Url = request.Url;
            LoadHeaders(request.Headers);
            LoadBody(request.Body);
            LoadCaptures(captures);
            AccessTokenVariable = auth.AccessTokenVariable ?? AuthDefaults.AccessTokenVariable;
            ExpiryVariable = auth.ExpiryVariable ?? AuthDefaults.ExpiryVariable;
            ExpiresInExpression = "$.expires_in";
            return;
        }

        Seed(AuthTemplateCatalog.Default);
    }

    /// <summary>Write the editor's state onto an <see cref="AuthConfig"/> as a template token request +
    /// captures. Leaves the legacy scalar OAuth2 fields untouched (they're ignored once
    /// <see cref="AuthConfig.TokenRequest"/> is set, and callers build a fresh config anyway).</summary>
    public void ApplyTo(AuthConfig auth)
    {
        auth.TokenRequest = new AuthTokenRequest
        {
            Method = string.IsNullOrWhiteSpace(Method) ? "POST" : Method,
            Url = Url,
            Headers = Headers.ToModel(),
            Body = Body.ToModel(),
        };
        auth.TokenCaptures = Captures.Select(c => c.ToModel()).ToList();
        auth.AccessTokenVariable = string.IsNullOrEmpty(AccessTokenVariable) ? null : AccessTokenVariable;
        auth.ExpiryVariable = string.IsNullOrEmpty(ExpiryVariable) ? null : ExpiryVariable;
        auth.ExpiresInExpression = string.IsNullOrEmpty(ExpiresInExpression) ? null : ExpiresInExpression;

        // The browser half. None of this was saved before, so a profile reopened with an empty
        // authorize URL and no provider - every session started by rediscovering the provider before
        // the sign-in button could do anything, and the pinned port (which the provider had been told
        // about) came back as ephemeral.
        auth.AuthorizeUrl = string.IsNullOrWhiteSpace(AuthorizeUrl) ? null : AuthorizeUrl;
        auth.AuthorizeParameters = AuthorizeParameters.ToModel().ToList();
        auth.RedirectPort = RedirectPort > 0 ? RedirectPort : null;
        auth.SignInProviderKey = SelectedProvider?.Key;
        auth.SignInTenant = string.IsNullOrWhiteSpace(Tenant) ? null : Tenant;
    }

    private void LoadSignIn(AuthConfig auth)
    {
        AuthorizeUrl = auth.AuthorizeUrl ?? "";
        LoadAuthorizeParameters(auth.AuthorizeParameters);
        RedirectPort = auth.RedirectPort ?? AuthorizationCodeFlow.EphemeralPort;
        Tenant = auth.SignInTenant ?? "";

        // Selects the template the provider corresponds to, since the provider is no longer picked
        // separately. An unknown key - a profile written by a newer version, or one whose provider was
        // removed - leaves whatever the token request already resolved to, rather than refusing to
        // load: everything the sign-in actually needs was saved as plain URLs and fields, so it still
        // works, and only the label is missing.
        if (SignInProviderCatalog.ByKey(auth.SignInProviderKey) is { } provider
            && TemplateOptions.FirstOrDefault(t => t.ProviderKey == provider.Key) is { } template)
        {
            SelectedTemplate = template;
        }
    }

    private bool ReadsAuthorizationCode() =>
        Body.UrlEncoded.Rows.Any(r =>
            (r.Value ?? "").Contains(SignInService.CodeVariable, StringComparison.Ordinal));

    /// <summary>A standalone OAuth2 <see cref="AuthConfig"/> for Test/Verify (Type + this editor's state).</summary>
    public AuthConfig ToAuthConfig()
    {
        var config = new AuthConfig { Type = AuthType.OAuth2 };
        ApplyTo(config);
        return config;
    }

    /// <summary>
    /// Fills the editor in from a template WITHOUT throwing away work already done.
    ///
    /// <para>It used to replace everything, so the natural order of doing this - Discover your
    /// endpoints, fill in your client id, then change your mind about the grant - lost all of it, and
    /// pressing Apply a second time after correcting one field lost the correction. What survives is
    /// decided by <see cref="TemplateSeedMerge"/>, in Core, where the rule can be tested without a
    /// UI; the short version is that a template seeds structure and never overwrites an answer only
    /// the user has.</para>
    ///
    /// <para><paramref name="replace"/> is for LOADING a saved profile, where there is no user work to
    /// protect and merging would blend two unrelated configurations.</para>
    /// </summary>
    private void Seed(AuthTemplate template, bool replace = false)
    {
        // The picker's selection must be one of ITS OWN options or the ComboBox shows its placeholder
        // instead - which is what happened after choosing a provider: a provider template is built on
        // the fly, and one built here is not reference- or value-equal to the cached list's copy.
        SelectedTemplate = TemplateOptions.FirstOrDefault(t => t.Key == template.Key)
            ?? TemplateOptions.FirstOrDefault(t => t.Grant == template.Grant)
            ?? template;

        Method = string.IsNullOrWhiteSpace(template.SeedRequest.Method) ? "POST" : template.SeedRequest.Method;

        Url = replace
            ? template.SeedRequest.Url
            : TemplateSeedMerge.Url(Url, template.SeedRequest.Url);

        // A provider seed brings a real authorize endpoint; the generic ones bring an empty string,
        // which must not wipe an endpoint Discover found.
        AuthorizeUrl = replace
            ? template.AuthorizeUrl
            : TemplateSeedMerge.Url(AuthorizeUrl, template.AuthorizeUrl);

        LoadHeaders(replace
            ? template.SeedRequest.Headers
            : TemplateSeedMerge.Fields(Headers.ToModel(), template.SeedRequest.Headers));

        LoadBody(template.SeedRequest.Body, replace);

        LoadCaptures(replace
            ? template.SeedCaptures
            : TemplateSeedMerge.Captures([.. Captures.Select(c => c.ToModel())], template.SeedCaptures));

        LoadAuthorizeParameters(replace
            ? template.AuthorizeParameters
            : TemplateSeedMerge.Fields(AuthorizeParameters.ToModel(), template.AuthorizeParameters));

        AccessTokenVariable = replace
            ? template.AccessTokenVariable
            : TemplateSeedMerge.Name(AccessTokenVariable, template.AccessTokenVariable);

        ExpiryVariable = replace
            ? template.ExpiryVariable
            : TemplateSeedMerge.Name(ExpiryVariable, template.ExpiryVariable);

        ExpiresInExpression = replace
            ? template.ExpiresInExpression ?? ""
            : TemplateSeedMerge.Name(ExpiresInExpression, template.ExpiresInExpression);
    }

    private void LoadAuthorizeParameters(IEnumerable<KeyValueItem> parameters)
    {
        AuthorizeParameters.Rows.Clear();
        foreach (var parameter in parameters)
        {
            AuthorizeParameters.AddRowQuietly(KeyValueRowViewModel.FromModel(parameter));
        }
    }

    private void LoadHeaders(IEnumerable<KeyValueItem> headers)
    {
        Headers.Rows.Clear();
        foreach (var header in headers)
        {
            Headers.AddRowQuietly(KeyValueRowViewModel.FromModel(header));
        }
    }

    private void LoadBody(RequestBody body, bool replace = true)
    {
        // A raw body is one blob with no keys to merge on, so it is only ever replaced - and only when
        // the template actually brings one, or switching to a form-encoded template would silently
        // erase a hand-written JSON login body.
        if (replace || !string.IsNullOrWhiteSpace(body.Raw))
        {
            Body.Raw = body.Raw ?? "";
        }

        Body.Type = body.Type;
        Body.BinaryFilePath = body.BinaryFilePath;

        Fill(Body.UrlEncoded, replace ? body.UrlEncoded : TemplateSeedMerge.Fields(Body.UrlEncoded.ToModel(), body.UrlEncoded));
        Fill(Body.FormData, replace ? body.FormData : TemplateSeedMerge.Fields(Body.FormData.ToModel(), body.FormData));

        static void Fill(KeyValueGridViewModel grid, IEnumerable<KeyValueItem> fields)
        {
            grid.Rows.Clear();
            foreach (var field in fields)
            {
                grid.AddRowQuietly(KeyValueRowViewModel.FromModel(field));
            }
        }
    }

    private void LoadCaptures(IEnumerable<CaptureRule> captures)
    {
        Captures.Clear();
        foreach (var capture in captures)
        {
            var row = new CaptureRowViewModel(capture);
            row.PropertyChanged += (_, _) => RaiseChanged();
            Captures.Add(row);
        }
    }

    private static bool IsLegacyConfigured(AuthConfig auth) =>
        !string.IsNullOrWhiteSpace(auth.TokenUrl)
        || !string.IsNullOrWhiteSpace(auth.ClientId)
        || !string.IsNullOrWhiteSpace(auth.ClientSecret)
        || !string.IsNullOrWhiteSpace(auth.RefreshToken)
        || !string.IsNullOrWhiteSpace(auth.Scopes);
}
