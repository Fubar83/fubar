namespace Fubar.Studio.Core.Auth;

/// <summary>
/// Default names for the session variables an OAuth2 flow reads/writes when the user hasn't named their
/// own. Domain-owned so both the auth provider (which populates them) and the header resolver (which
/// references them) share one source of truth, rather than the UI reaching into an Infrastructure const.
/// </summary>
public static class AuthDefaults
{
    public const string AccessTokenVariable = "oauth2_access_token";

    public const string ExpiryVariable = "oauth2_expires_at";
}
