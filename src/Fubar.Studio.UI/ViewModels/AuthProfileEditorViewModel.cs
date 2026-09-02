using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.Controls;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Opened in the main canvas (in place of the Request Editor) when a Left Pane Auth Profiles row is
/// clicked for editing. Unlike the Request Editor's Auth tab (which also offers Inherit/None/a named
/// Profile), a profile itself is always one concrete scheme - Bearer/API Key/Basic/OAuth 2.0. OAuth 2.0
/// is edited through the shared request-builder-style <see cref="OAuth2"/> child.
/// </summary>
public partial class AuthProfileEditorViewModel : ViewModelBase
{
    private readonly Workspace _workspace;
    private readonly IAuthProfileStore _workspaceService;
    private readonly IAuthProvider _authProvider;
    private readonly StatusLogViewModel _statusLog;
    private readonly string _profileId;
    private readonly EnvironmentManagerViewModel _environmentManager;

    public static IReadOnlyList<AuthType> TypeOptions { get; } = [AuthType.Bearer, AuthType.ApiKey, AuthType.Basic, AuthType.OAuth2];

    public static IReadOnlyList<ApiKeyLocation> ApiKeyLocationOptions { get; } = Enum.GetValues<ApiKeyLocation>();

    public AuthProfileEditorViewModel(
        AuthProfile profile,
        Workspace workspace,
        IAuthProfileStore workspaceService,
        IAuthProvider authProvider,
        IVariableResolver variableResolver,
        IFilePickerService filePickerService,
        IJsonSchemaValidator schemaValidator,
        StatusLogViewModel statusLog,
        EnvironmentManagerViewModel environmentManager,
        IOpenIdDiscoveryService discovery)
    {
        _workspace = workspace;
        _workspaceService = workspaceService;
        _authProvider = authProvider;
        _statusLog = statusLog;
        _profileId = profile.Id;
        _environmentManager = environmentManager;

        OAuth2 = new TokenRequestEditorViewModel(filePickerService, schemaValidator)
        {
            // Test and Verify resolve against the ACTIVE ENVIRONMENT, exactly as a real request will.
            //
            // They used to pass null, on the reasoning that a profile has no environment of its own.
            // True, and beside the point: a profile is only ever used from a request, and a request
            // runs under an environment - so testing without one tested something that never happens.
            // It bit hardest on OAuth, where the token URL, client id and client secret are the very
            // things that differ between dev, staging and production, and are therefore exactly what
            // people put in an environment. The unresolved {{token}} then travelled into the token
            // request as literal text and came back as an invalid-URI error that said nothing about
            // variables.
            //
            // Read INSIDE the lambda, never captured: the user can switch environment while an editor
            // is open, and a Test that quietly used whichever one was selected when the editor opened
            // is the same class of confusion in a new place.
            TestAuthHandler = async config =>
                (await _authProvider.PrepareAsync(config, _workspace, _environmentManager.ActiveEnvironment)).Outcome,
            PreviewHandler = config =>
                _authProvider.PreviewTokenRequest(config, _workspace, _environmentManager.ActiveEnvironment),
            DiscoveryHandler = issuer => discovery.DiscoverAsync(issuer),
            VariableContext = new VariableTooltipContext(
                variableResolver, workspace, environmentManager.ActiveEnvironment, SecretsRevealed: false),
        };

        Name = profile.Name;
        Type = TypeOptions.Contains(profile.Config.Type) ? profile.Config.Type : AuthType.Bearer;
        Token = profile.Config.Token ?? "";
        ApiKeyName = profile.Config.ApiKeyName ?? "";
        ApiKeyValue = profile.Config.ApiKeyValue ?? "";
        ApiKeyLocation = profile.Config.ApiKeyLocation;
        Username = profile.Config.Username ?? "";
        Password = profile.Config.Password ?? "";
        OAuth2.LoadFrom(profile.Config);
    }

    /// <summary>The workspace this profile belongs to - used by MainViewModel to clear the main
    /// canvas when that workspace's tab is closed.</summary>
    public Workspace Workspace => _workspace;

    /// <summary>Used by MainViewModel to highlight this profile's row in the Left Pane while it's
    /// the active canvas surface.</summary>
    public string ProfileId => _profileId;

    /// <summary>The request-builder-style OAuth2 token-request editor (shown when <see cref="Type"/> is OAuth2).</summary>
    public TokenRequestEditorViewModel OAuth2 { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial AuthType Type { get; set; }

    public bool IsBearerVisible => Type == AuthType.Bearer;

    public bool IsApiKeyVisible => Type == AuthType.ApiKey;

    public bool IsBasicVisible => Type == AuthType.Basic;

    public bool IsOAuth2Visible => Type == AuthType.OAuth2;

    partial void OnTypeChanged(AuthType value)
    {
        OnPropertyChanged(nameof(IsBearerVisible));
        OnPropertyChanged(nameof(IsApiKeyVisible));
        OnPropertyChanged(nameof(IsBasicVisible));
        OnPropertyChanged(nameof(IsOAuth2Visible));
    }

    [ObservableProperty]
    public partial string Token { get; set; }

    [ObservableProperty]
    public partial string ApiKeyName { get; set; }

    [ObservableProperty]
    public partial string ApiKeyValue { get; set; }

    [ObservableProperty]
    public partial ApiKeyLocation ApiKeyLocation { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    private AuthConfig BuildConfig()
    {
        var config = new AuthConfig
        {
            Type = Type,
            Token = string.IsNullOrEmpty(Token) ? null : Token,
            ApiKeyName = string.IsNullOrEmpty(ApiKeyName) ? null : ApiKeyName,
            ApiKeyValue = string.IsNullOrEmpty(ApiKeyValue) ? null : ApiKeyValue,
            ApiKeyLocation = ApiKeyLocation,
            Username = string.IsNullOrEmpty(Username) ? null : Username,
            Password = string.IsNullOrEmpty(Password) ? null : Password,
        };

        OAuth2.ApplyTo(config);
        return config;
    }

    /// <summary>Raised after a successful Save - the Left Pane's Auth Profiles section refreshes from it.</summary>
    public event Action? Saved;

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var profiles = (await _workspaceService.LoadAuthProfilesAsync(_workspace.RootPath)).ToList();
            var index = profiles.FindIndex(p => p.Id == _profileId);

            var updated = new AuthProfile
            {
                Id = _profileId,
                Name = Name,
                Config = BuildConfig(),
            };

            if (index >= 0)
            {
                profiles[index] = updated;
            }
            else
            {
                profiles.Add(updated);
            }

            await _workspaceService.SaveAuthProfilesAsync(_workspace.RootPath, profiles);
            _statusLog.Log($"Saved auth profile \"{Name}\".");
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Failed to save auth profile \"{Name}\": {ex.Message}");
        }
    }
}
