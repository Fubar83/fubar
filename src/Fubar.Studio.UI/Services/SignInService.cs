using System;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Runs the browser half of an authorization-code sign-in and leaves the code where the token request
/// can read it.
///
/// The two halves are deliberately separate. The browser round trip is not a request and cannot be
/// expressed as one; the exchange that follows is an ordinary request and stays fully editable, which
/// is what lets a provider needing one extra field be handled by adding it rather than by waiting for
/// this app to grow a setting.
///
/// What passes between them are SESSION variables - <c>oauth2_code</c>,
/// <c>oauth2_code_verifier</c>, <c>oauth2_redirect_uri</c> - held in memory for this
/// (workspace, environment) and never written to disk. An authorization code is a bearer credential
/// for the seconds it lives, and a verifier is the secret PKCE exists to protect; neither belongs in
/// a file anyone might commit.
/// </summary>
public sealed class SignInService
{
    private readonly IAuthorizationCodeListener _listener;
    private readonly ISessionVariableStore _session;

    public SignInService(IAuthorizationCodeListener listener, ISessionVariableStore session)
    {
        _listener = listener;
        _session = session;
    }

    /// <summary>Names the session variables the authorization-code template reads.</summary>
    public const string CodeVariable = "oauth2_code";

    public const string VerifierVariable = "oauth2_code_verifier";

    public const string RedirectUriVariable = "oauth2_redirect_uri";

    public async Task<SignInResult> SignInAsync(
        string authorizeUrl,
        string clientId,
        string? scopes,
        Workspace workspace,
        WorkspaceEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        var scope = SessionScope.For(workspace, activeEnvironment);

        AuthorizationRequest request;

        try
        {
            request = AuthorizationCodeFlow.Build(authorizeUrl, clientId, scopes, _listener.ReservePort());
        }
        catch (ArgumentException)
        {
            return new SignInResult(false, "Set the authorize URL first.", null);
        }

        // Stored BEFORE the browser opens, so a redirect that arrives while the user is still deciding
        // has somewhere to land, and so the URI is on screen to copy into the provider's registration
        // even if this attempt fails for that exact reason.
        _session.Set(scope, RedirectUriVariable, request.RedirectUri);

        var callback = await _listener.ListenAsync(request, cancellationToken).ConfigureAwait(false);

        if (!callback.Ok)
        {
            // Clear rather than leave stale: a code from a previous attempt is expired or already
            // spent, and exchanging one produces an error about the code that says nothing about the
            // sign-in having been abandoned.
            _session.Set(scope, CodeVariable, null);
            _session.Set(scope, VerifierVariable, null);

            var detail = string.IsNullOrWhiteSpace(callback.ErrorDescription)
                ? callback.Error
                : $"{callback.Error}: {callback.ErrorDescription}";

            return new SignInResult(false, $"Sign-in failed - {detail}", request.RedirectUri);
        }

        _session.Set(scope, CodeVariable, callback.Code);
        _session.Set(scope, VerifierVariable, request.Verifier);

        return new SignInResult(
            true,
            "Signed in. Press Test / Get token to exchange the code for a token.",
            request.RedirectUri);
    }
}
