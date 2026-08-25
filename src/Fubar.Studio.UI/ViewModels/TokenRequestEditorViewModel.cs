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

    private void RaiseChanged() => Changed?.Invoke();

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
    }

    [RelayCommand]
    private void VerifyRequest() => RequestPreview = PreviewHandler?.Invoke(ToAuthConfig());

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
