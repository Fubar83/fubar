using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// The providers offered by "Sign in with…".
///
/// <para>Short on purpose. Every entry here is a claim about somebody else's service that this
/// repository cannot test in CI and that will eventually go stale, so the bar is: the provider is one
/// people actually sign in to, and the preset saves them something a discovery document cannot. Okta,
/// Auth0, Keycloak, Entra B2C and every other OIDC provider are covered by <see cref="Custom"/> -
/// paste the issuer, press Discover, and the endpoints and scopes arrive from the provider itself,
/// which is fresher than anything hard-coded here.</para>
///
/// <para>Nothing here is locked afterwards. A preset seeds the same editable request the manual path
/// produces, so a provider that changes something can be corrected in the editor rather than waited
/// on.</para>
/// </summary>
public static class SignInProviderCatalog
{
    public static IReadOnlyList<SignInProvider> All { get; } =
    [
        Google(),
        Microsoft(),
        GitHub(),
        Custom(),
    ];

    /// <summary>What the picker starts on: the provider that needs the least explaining.</summary>
    public static SignInProvider Default => All[0];

    /// <summary>The provider a saved profile named, or null if it named none (or one since removed).</summary>
    public static SignInProvider? ByKey(string? key) =>
        key is null ? null : All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));

    /// <summary>The one entry that is not a preset at all - an issuer to paste and discover.</summary>
    public static SignInProvider CustomProvider => All[^1];

    private static SignInProvider Google() => new(
        Key: "google",
        DisplayName: "Google",
        IssuerTemplate: "https://accounts.google.com",
        AuthorizeEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint: "https://oauth2.googleapis.com/token",
        DefaultScopes: ["openid", "email", "profile"],

        // The two parameters that decide whether this sign-in can ever be renewed. Google returns a
        // refresh token ONLY with access_type=offline, and only re-issues one when consent is asked
        // for again - so without prompt=consent the second sign-in for an account silently comes back
        // with no refresh token at all, and the failure shows up hours later as an expired token that
        // cannot be refreshed.
        AuthorizeParameters:
        [
            new KeyValueItem { Key = "access_type", Value = "offline" },
            new KeyValueItem { Key = "prompt", Value = "consent" },
        ],

        // Google issues a client secret even for "Desktop app" clients and requires it in the
        // exchange. It is not a secret in the usual sense - it ships inside installed applications -
        // but omitting it fails the exchange with invalid_client, which reads like a wrong client id.
        NeedsClientSecret: true,
        TenantLabel: null,
        TenantDefault: null,
        TenantHelp: null,
        SetupSummary:
            "In Google Cloud Console → APIs & Services → Credentials, create an OAuth client ID of type "
            + "\"Desktop app\". Copy the client ID and client secret below. Desktop clients accept any "
            + "loopback port, so the redirect URI needs no registration.",
        ConsoleUrl: "https://console.cloud.google.com/apis/credentials");

    private static SignInProvider Microsoft() => new(
        Key: "microsoft",
        DisplayName: "Microsoft (Entra ID)",
        IssuerTemplate: "https://login.microsoftonline.com/{tenant}/v2.0",
        AuthorizeEndpoint: "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize",
        TokenEndpoint: "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",

        // offline_access is what asks for a refresh token here - Microsoft's equivalent of Google's
        // access_type, and a scope rather than a parameter.
        DefaultScopes: ["openid", "profile", "email", "offline_access"],
        AuthorizeParameters: [],

        // A public client must NOT send one. Entra rejects a secret from a client registered as public
        // with an error about the client type, and registering a desktop app as confidential to get
        // around that means shipping a real secret to every desk.
        NeedsClientSecret: false,
        TenantLabel: "Directory (tenant) ID",
        TenantDefault: "common",
        TenantHelp:
            "Your tenant ID or domain, or one of: common (any account), organizations (work/school), "
            + "consumers (personal).",
        SetupSummary:
            "In the Entra admin centre → App registrations → New registration, add a redirect URI under "
            + "\"Mobile and desktop applications\". Entra accepts any port on http://localhost, so "
            + "register http://localhost and leave the port below alone.",
        ConsoleUrl: "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade");

    private static SignInProvider GitHub() => new(
        Key: "github",
        DisplayName: "GitHub",
        IssuerTemplate: null, // publishes no discovery document
        AuthorizeEndpoint: "https://github.com/login/oauth/authorize",
        TokenEndpoint: "https://github.com/login/oauth/access_token",
        DefaultScopes: ["read:user"],
        AuthorizeParameters: [],
        NeedsClientSecret: true,
        TenantLabel: null,
        TenantDefault: null,
        TenantHelp: null,
        SetupSummary:
            "In GitHub → Settings → Developer settings → OAuth Apps, create an app and set its "
            + "\"Authorization callback URL\" to the redirect URI shown below. GitHub matches it "
            + "exactly, so pin the port rather than letting it change each time.",
        ConsoleUrl: "https://github.com/settings/developers",

        // Said out loud rather than left for someone to infer from the absence of a code_challenge in
        // the URL. The request still carries one - GitHub ignores unknown parameters - but an ignored
        // challenge protects nothing, so the client secret is doing the work instead.
        Caveat:
            "GitHub is OAuth 2.0 but not OpenID Connect: there is no discovery document and no ID "
            + "token, and it ignores PKCE, so the client secret is what proves this app is the one "
            + "asking. Its token endpoint returns form-encoded data unless asked for JSON, which is "
            + "why the seeded request sends Accept: application/json.");

    private static SignInProvider Custom() => new(
        Key: "custom",
        DisplayName: "Custom (any OpenID Connect provider)",
        IssuerTemplate: null,
        AuthorizeEndpoint: null,
        TokenEndpoint: null,
        DefaultScopes: ["openid", "profile", "email"],
        AuthorizeParameters: [],
        NeedsClientSecret: false,
        TenantLabel: null,
        TenantDefault: null,
        TenantHelp: null,
        SetupSummary:
            "Paste your provider's issuer URL and press Discover — Okta, Auth0, Keycloak, Entra B2C and "
            + "anything else publishing /.well-known/openid-configuration will fill in its own "
            + "endpoints and scopes. Register the redirect URI below with that provider.",
        ConsoleUrl: null,
        Caveat:
            "Some providers need an extra authorize parameter before they issue a usable token — Auth0 "
            + "wants audience set to your API identifier, or it returns an opaque token instead of a "
            + "JWT. Add it under \"Extra authorize parameters\".");
}
