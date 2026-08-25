using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the Auth tab (RequestEditorPane.md §5): inheritance selector (Inherit from parent folder /
/// None / Bearer / API Key / Basic / OAuth 2.0 / a named workspace <see cref="AuthProfile"/>) plus
/// the fields for whichever inline scheme is selected. Bearer/API Key/Basic are simple inline forms;
/// OAuth 2.0 is edited through the request-builder-style <see cref="OAuth2"/> child. Applying any of
/// this to an outgoing request is the <c>IAuthProvider</c>'s concern - this view model is editable state.
/// </summary>
public partial class RequestAuthViewModel : ViewModelBase
{
    public RequestAuthViewModel(TokenRequestEditorViewModel oauth2)
    {
        OAuth2 = oauth2;
    }

    public static IReadOnlyList<AuthType> TypeOptions { get; } = Enum.GetValues<AuthType>();

    public static IReadOnlyList<ApiKeyLocation> ApiKeyLocationOptions { get; } = Enum.GetValues<ApiKeyLocation>();

    /// <summary>The request-builder-style OAuth2 token-request editor (shown when <see cref="Type"/> is OAuth2).</summary>
    public TokenRequestEditorViewModel OAuth2 { get; }

    [ObservableProperty]
    public partial AuthType Type { get; set; } = AuthType.Inherit;

    public bool IsBearerVisible => Type == AuthType.Bearer;

    public bool IsApiKeyVisible => Type == AuthType.ApiKey;

    public bool IsBasicVisible => Type == AuthType.Basic;

    public bool IsOAuth2Visible => Type == AuthType.OAuth2;

    public bool IsProfileVisible => Type == AuthType.Profile;

    partial void OnTypeChanged(AuthType value)
    {
        OnPropertyChanged(nameof(IsBearerVisible));
        OnPropertyChanged(nameof(IsApiKeyVisible));
        OnPropertyChanged(nameof(IsBasicVisible));
        OnPropertyChanged(nameof(IsOAuth2Visible));
        OnPropertyChanged(nameof(IsProfileVisible));
    }

    [ObservableProperty]
    public partial string Token { get; set; } = "";

    [ObservableProperty]
    public partial string ApiKeyName { get; set; } = "";

    [ObservableProperty]
    public partial string ApiKeyValue { get; set; } = "";

    [ObservableProperty]
    public partial ApiKeyLocation ApiKeyLocation { get; set; } = ApiKeyLocation.Header;

    [ObservableProperty]
    public partial string Username { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    /// <summary>Workspace-level reusable auth profiles, offered when <see cref="Type"/> is <see cref="AuthType.Profile"/>.</summary>
    public ObservableCollection<AuthProfile> AvailableProfiles { get; } = [];

    [ObservableProperty]
    public partial AuthProfile? SelectedProfile { get; set; }

    public AuthConfig ToModel()
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

    /// <summary>Populate this view model (and its OAuth2 child) from a stored <see cref="AuthConfig"/>.</summary>
    public void LoadFrom(AuthConfig auth)
    {
        Type = auth.Type;
        Token = auth.Token ?? "";
        ApiKeyName = auth.ApiKeyName ?? "";
        ApiKeyValue = auth.ApiKeyValue ?? "";
        ApiKeyLocation = auth.ApiKeyLocation;
        Username = auth.Username ?? "";
        Password = auth.Password ?? "";
        OAuth2.LoadFrom(auth);
    }
}
