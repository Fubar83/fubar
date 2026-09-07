using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Opens the browser, waits for the redirect, and stores the code.
    /// </summary>
    /// <param name="request">Everything about the provider this attempt needs - see <see cref="SignInRequest"/>.</param>
    /// <param name="workspace">The workspace whose session scope the code is stored in.</param>
    /// <param name="activeEnvironment">Its active environment, which is part of that scope.</param>
    /// <param name="cancellationToken">Cancelling is the ordinary ending, not an exceptional one.</param>
    public async Task<SignInResult> SignInAsync(
        SignInRequest request,
        Workspace workspace,
        WorkspaceEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = SessionScope.For(workspace, activeEnvironment);

        // A pinned port is used as given; zero means "ask the OS", which is the only case where the
        // redirect URI cannot be known before the listener binds.
        var port = request.RedirectPort > 0 ? request.RedirectPort : _listener.ReservePort();

        AuthorizationRequest authorization;

        try
        {
            authorization = AuthorizationCodeFlow.Build(
                request.AuthorizeUrl,
                request.ClientId,
                request.Scopes,
                port,
                extraParameters: request.ExtraParameters);
        }
        catch (ArgumentException)
        {
            return new SignInResult(false, "Set the authorize URL first.", null);
        }

        // Stored BEFORE the browser opens, so a redirect that arrives while the user is still deciding
        // has somewhere to land, and so the URI is on screen to copy into the provider's registration
        // even if this attempt fails for that exact reason.
        _session.Set(scope, RedirectUriVariable, authorization.RedirectUri);

        // A previous attempt's code is expired or already spent. Clearing before rather than only
        // after means an abandoned sign-in cannot leave one behind for the next exchange to fail on -
        // with an error about the code, which says nothing about a sign-in nobody finished.
        ClearCode(scope);

        var callback = await _listener.ListenAsync(authorization, cancellationToken).ConfigureAwait(false);

        if (!callback.Ok)
        {
            var detail = string.IsNullOrWhiteSpace(callback.ErrorDescription)
                ? callback.Error
                : $"{callback.Error}: {callback.ErrorDescription}";

            return new SignInResult(false, $"Sign-in failed - {detail}", authorization.RedirectUri);
        }

        _session.Set(scope, CodeVariable, callback.Code);
        _session.Set(scope, VerifierVariable, authorization.Verifier);

        return new SignInResult(
            true,
            "Signed in. Press Test / Get token to exchange the code for a token.",
            authorization.RedirectUri);
    }

    private void ClearCode(string scope)
    {
        _session.Set(scope, CodeVariable, null);
        _session.Set(scope, VerifierVariable, null);
    }
}

/// <summary>
/// What one sign-in attempt needs to know about the provider.
///
/// <para>A record rather than six positional arguments because it grew from three to six while this
/// feature was being built, and a call site passing <c>(url, id, scopes, null, 0, null)</c> is one
/// transposition away from silently signing in to the wrong place.</para>
/// </summary>
/// <param name="AuthorizeUrl">Where to send the browser.</param>
/// <param name="ClientId">The client id, as the token request will also carry it.</param>
/// <param name="Scopes">Space-separated scopes, or null to ask for none.</param>
/// <param name="AuthorizeParameters">Provider-specific authorize parameters, disabled rows and all.</param>
/// <param name="RedirectPort">A pinned loopback port, or 0 to ask the OS for a free one.</param>
public sealed record SignInRequest(
    string AuthorizeUrl,
    string ClientId,
    string? Scopes,
    IReadOnlyList<KeyValueItem>? AuthorizeParameters = null,
    int RedirectPort = AuthorizationCodeFlow.EphemeralPort)
{
    /// <summary>
    /// The extras as the flow wants them: enabled rows with a key, never null.
    ///
    /// A disabled row is honoured as disabled rather than sent anyway - the grid's tick box is the
    /// only way to try a sign-in without a parameter you suspect, and one that did nothing would make
    /// that diagnosis impossible.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ExtraParameters =>
        [.. (AuthorizeParameters ?? [])
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key))
            .Select(p => new KeyValuePair<string, string>(p.Key, p.Value))];
}
