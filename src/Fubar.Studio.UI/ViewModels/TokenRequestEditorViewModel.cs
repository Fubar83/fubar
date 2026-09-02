using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
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

        Headers.Changed += RaiseChanged;
        Body.Changed += RaiseChanged;
        Body.PropertyChanged += (_, _) => RaiseChanged();
        Captures.CollectionChanged += (_, _) => RaiseChanged();
    }

    public static IReadOnlyList<AuthTemplate> TemplateOptions => AuthTemplateCatalog.All;

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

    [RelayCommand]
    private void ApplyTemplate()
    {
        if (SelectedTemplate is { } template)
        {
            Seed(template);
            RaiseChanged();
        }
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

    partial void OnResponseFieldsChanged(IReadOnlyList<TokenResponseField> value) =>
        OnPropertyChanged(nameof(HasResponseFields));

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
    private void CaptureField(TokenResponseField? field)
    {
        if (field is null || Captures.Any(c => string.Equals(c.Expression, field.Path, StringComparison.Ordinal)))
        {
            return;
        }

        var leaf = field.Path[(field.Path.LastIndexOf('.') + 1)..];

        // The access token gets the variable the Bearer header already reads, so the commonest case
        // is wired up correctly by one click rather than by knowing that convention.
        var variable = leaf is "access_token" or "id_token"
            ? (string.IsNullOrWhiteSpace(AccessTokenVariable) ? AuthDefaults.AccessTokenVariable : AccessTokenVariable)
            : leaf;

        var row = new CaptureRowViewModel(new CaptureRule
        {
            Enabled = true,
            VariableName = variable,
            Source = ResponseField.JsonBody,
            Expression = field.Path,
            Scope = CaptureScope.Session,
        });

        row.PropertyChanged += (_, _) => RaiseChanged();
        Captures.Add(row);
        RaiseChanged();
    }

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
    public Func<string, string, string?, Task<SignInResult>>? SignInHandler { get; set; }

    /// <summary>The provider's authorize endpoint. Filled by Discover when the provider publishes one.</summary>
    [ObservableProperty]
    public partial string AuthorizeUrl { get; set; } = "";

    /// <summary>
    /// The redirect this app will listen on, shown BEFORE the flow can work.
    ///
    /// It has to be registered with the provider exactly as written, and a sign-in that fails because
    /// it was not is the single most opaque failure in this grant - the browser shows the provider's
    /// own error page and the app never hears anything at all. So the value is on screen to copy
    /// rather than discovered from a failure.
    /// </summary>
    [ObservableProperty]
    public partial string RedirectUri { get; private set; } = "";

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

        var result = await SignInHandler(AuthorizeUrl, ClientIdInBody(), scopes);

        RedirectUri = result.RedirectUri ?? RedirectUri;
        SignInStatus = result.Message;
    }

    /// <summary>
    /// The client id as the token request carries it, so the browser step and the exchange cannot
    /// disagree about who is asking - a mismatch there is rejected by the provider with an error about
    /// the code rather than about the client.
    /// </summary>
    private string ClientIdInBody() =>
        Body.UrlEncoded.Rows.FirstOrDefault(r => string.Equals(r.Key, "client_id", StringComparison.OrdinalIgnoreCase))?.Value
        ?? "";

    partial void OnSelectedTemplateChanged(AuthTemplate? value) => OnPropertyChanged(nameof(IsAuthorizationCode));

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
    }

    /// <summary>A standalone OAuth2 <see cref="AuthConfig"/> for Test/Verify (Type + this editor's state).</summary>
    public AuthConfig ToAuthConfig()
    {
        var config = new AuthConfig { Type = AuthType.OAuth2 };
        ApplyTo(config);
        return config;
    }

    private void Seed(AuthTemplate template)
    {
        SelectedTemplate = template;
        Method = string.IsNullOrWhiteSpace(template.SeedRequest.Method) ? "POST" : template.SeedRequest.Method;
        Url = template.SeedRequest.Url;
        LoadHeaders(template.SeedRequest.Headers);
        LoadBody(template.SeedRequest.Body);
        LoadCaptures(template.SeedCaptures);
        AccessTokenVariable = template.AccessTokenVariable;
        ExpiryVariable = template.ExpiryVariable;
        ExpiresInExpression = template.ExpiresInExpression ?? "";
    }

    private void LoadHeaders(IEnumerable<KeyValueItem> headers)
    {
        Headers.Rows.Clear();
        foreach (var header in headers)
        {
            Headers.AddRowQuietly(KeyValueRowViewModel.FromModel(header));
        }
    }

    private void LoadBody(RequestBody body)
    {
        Body.Type = body.Type;
        Body.Raw = body.Raw ?? "";
        Body.BinaryFilePath = body.BinaryFilePath;
        Body.UrlEncoded.Rows.Clear();
        foreach (var field in body.UrlEncoded)
        {
            Body.UrlEncoded.AddRowQuietly(KeyValueRowViewModel.FromModel(field));
        }

        Body.FormData.Rows.Clear();
        foreach (var field in body.FormData)
        {
            Body.FormData.AddRowQuietly(KeyValueRowViewModel.FromModel(field));
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
