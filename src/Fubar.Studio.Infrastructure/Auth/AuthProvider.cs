using System.Text;
using System.Text.Json.Nodes;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Variables;
using Json.Path;

namespace Fubar.Studio.Infrastructure.Auth;

/// <summary>
/// The auth prestep: <b>Acquire</b> (OAuth 2.0 only) then <b>Apply</b> (all schemes). Acquire reuses a
/// cached, unexpired token from the <b>per-(workspace,environment)</b> session, else mints one - down
/// ONE path. The token request is an ordinary request run through the normal HTTP executor;
/// <see cref="AuthConfig.TokenCaptures"/> (JSONPath → session variables) are applied on a 2xx and
/// <b>cleared on failure</b>. A legacy fixed-form config (no <see cref="AuthConfig.TokenRequest"/>) is
/// upgraded to that shape on the way in - see <c>Upgraded</c> - rather than served by a second engine,
/// because two engines behind an invisible switch is how a guard ends up on the branch nobody is on.
/// Apply then resolves each scheme's variables and produces the <see cref="AppliedAuth"/> (Bearer/OAuth2
/// bearer header, API key header/query, HTTP Basic) the pipeline injects into the request. Also backs the
/// Auth tab's Test button.
/// </summary>
public sealed class AuthProvider : IAuthProvider
{
    // Tokens are re-fetched this long before they actually expire, to avoid sending a just-expired one.
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(30);

    private readonly IVariableResolver _resolver;
    private readonly ISessionVariableStore _session;
    private readonly IExecutorRegistry _executorRegistry;
    private readonly IResponseTestService _testService;

    public AuthProvider(
        IVariableResolver resolver,
        ISessionVariableStore session,
        IExecutorRegistry executorRegistry,
        IResponseTestService testService)
    {
        _resolver = resolver;
        _session = session;
        _executorRegistry = executorRegistry;
        _testService = testService;
    }

    public async Task<AuthPreparation> PrepareAsync(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment, bool forceReacquire = false, CancellationToken cancellationToken = default)
    {
        // 1. Acquire (OAuth2 only) - mint/reuse a token in the per-environment session.
        var outcome = new AuthOutcome(true, "");
        if (auth.Type == AuthType.OAuth2)
        {
            var scope = SessionScope.For(workspace, activeEnvironment);

            // ONE engine. A config with no TokenRequest is upgraded to one here and run down the same
            // path as everything else, rather than through a second implementation.
            //
            // Two engines behind an invisible switch is how a guard gets added to the branch nobody is
            // on: the unresolved-variable check went into the legacy path first, and every test for it
            // built a legacy config, so it passed while the path the editor writes stayed unguarded.
            // The editor already upgrades on open and persists on save, so this only serves profiles
            // nobody has edited since the template editor arrived.
            outcome = await AcquireTemplateAsync(
                Upgraded(auth, workspace, activeEnvironment), workspace, activeEnvironment, scope, forceReacquire, cancellationToken);
        }

        // 2. Apply - resolve every scheme's fields (+ the acquired token) into headers/query.
        return new AuthPreparation(Apply(auth, workspace, activeEnvironment), outcome);
    }

    /// <summary>
    /// A config guaranteed to carry a token request, upgrading a legacy fixed-form one if it does not.
    ///
    /// The credentials resolver is passed through so an <see cref="OAuth2ClientAuth.BasicHeader"/>
    /// config keeps sending Basic: that header is base64 of <c>id:secret</c> and cannot be built while
    /// those are still <c>{{tokens}}</c>, so it can only be produced here, where the environment is
    /// known. Nothing about this is persisted - it is a view of the config for this one acquisition.
    /// </summary>
    private AuthConfig Upgraded(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment)
    {
        if (auth.TokenRequest is not null)
        {
            return auth;
        }

        var (request, captures) = OAuth2LegacyTemplate.FromLegacy(
            auth,
            value => _resolver.Substitute(value, workspace, activeEnvironment));

        return new AuthConfig
        {
            Type = auth.Type,
            AccessTokenVariable = auth.AccessTokenVariable,
            ExpiryVariable = auth.ExpiryVariable,
            TokenRequest = request,
            TokenCaptures = captures,

            // The legacy engine read expires_in through the token service rather than a configured
            // path, so an upgraded config has to be told where it is or every token would look
            // non-expiring and be cached forever.
            ExpiresInExpression = auth.ExpiresInExpression ?? "$.expires_in",
        };
    }

    public AppliedAuth Apply(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment)
    {
        string Resolve(string? value) => _resolver.Substitute(value, workspace, activeEnvironment);

        var accessToken = auth.Type == AuthType.OAuth2
            ? _session.Get(SessionScope.For(workspace, activeEnvironment), EffectiveTokenVariable(auth))
            : null;

        var resolved = new ResolvedAuth(
            auth.Type,
            Token: Resolve(auth.Token),
            AccessToken: accessToken,
            ApiKeyName: Resolve(auth.ApiKeyName),
            ApiKeyValue: Resolve(auth.ApiKeyValue),
            auth.ApiKeyLocation,
            Username: Resolve(auth.Username),
            Password: Resolve(auth.Password));

        return AuthApplier.Build(resolved);
    }

    // --- Acquire: template path (an editable request + capture rules) ----------------------------------

    private async Task<AuthOutcome> AcquireTemplateAsync(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment, string scope, bool forceReacquire, CancellationToken cancellationToken)
    {
        var tokenVariable = EffectiveTokenVariable(auth);
        var expiryVariable = EffectiveExpiryVariable(auth);

        // Reuse a cached, still-valid token (unless a re-acquire is forced, e.g. after a 401).
        var cachedToken = _session.Get(scope, tokenVariable);
        if (!forceReacquire && !string.IsNullOrEmpty(cachedToken) && !IsExpired(_session.Get(scope, expiryVariable)))
        {
            return new AuthOutcome(true, "Using the cached token (still valid).");
        }

        var tokenRequest = auth.TokenRequest!;
        if (string.IsNullOrWhiteSpace(_resolver.Substitute(tokenRequest.Url, workspace, activeEnvironment)))
        {
            return new AuthOutcome(false, "The token request needs a URL.");
        }

        // The same guard the legacy path has, and this is the one that matters: the EDITOR writes a
        // TokenRequest, so this is the path nearly every user is on. Without it an unresolved
        // {{token_url}} is handed to the HTTP executor and comes back as a transport error naming a
        // malformed URI - the exact confusion the legacy guard was added to remove, still present on
        // the path people actually take.
        if (UnresolvedVariables.Describe(
                [.. TokenRequestVariables
                    .Of(tokenRequest, text => _resolver.Substitute(text, workspace, activeEnvironment) ?? text)
                    .Where(v => !v.IsResolved)
                    .Select(v => v.Name)]) is { } unresolvedInRequest)
        {
            return new AuthOutcome(false, $"Token request: {unresolvedInRequest}");
        }

        var model = new RequestModel
        {
            Name = "auth-token",
            Kind = RequestKind.Http,
            Method = string.IsNullOrWhiteSpace(tokenRequest.Method) ? "POST" : tokenRequest.Method,
            Url = tokenRequest.Url,
            Headers = tokenRequest.Headers,
            Body = tokenRequest.Body,
        };

        ExecutionResult result;
        try
        {
            var executor = _executorRegistry.Resolve(RequestKind.Http);
            result = await executor.ExecuteAsync(model, new RequestExecutionContext(workspace, activeEnvironment), cancellationToken);
        }
        catch (Exception ex)
        {
            ClearCapturedVariables(auth, scope, tokenVariable, expiryVariable);
            return new AuthOutcome(false, $"Token request failed: {ex.Message}. Cleared captured variables.");
        }

        // "Success" here is a real 2xx - ExecutionResult.IsSuccess is only about transport (a 400+body is
        // "successful" transport but a failed auth), so check the status explicitly.
        var succeeded = result.ErrorMessage is null && result.StatusCode is >= 200 and < 300;
        if (!succeeded)
        {
            ClearCapturedVariables(auth, scope, tokenVariable, expiryVariable);
            var reason = result.ErrorMessage ?? $"token endpoint returned {result.StatusCode}";
            return new AuthOutcome(false, $"Token request failed: {reason}. Cleared captured variables.")
            {
                // Especially on failure: this is the body that says invalid_client, and why.
                Response = new TokenResponse(result.StatusCode, result.Body),
            };
        }

        // Apply the capture rules (forced to session scope - tokens must never touch disk).
        var captures = auth.TokenCaptures
            .Where(c => c.Enabled)
            .Select(c => new CaptureRule
            {
                Enabled = true,
                VariableName = c.VariableName,
                Source = c.Source,
                Expression = c.Expression,
                Scope = CaptureScope.Session,
            })
            .ToList();
        await _testService.ApplyCapturesAsync(captures, result, workspace, activeEnvironment, cancellationToken);

        // Compute the token's expiry (for caching) from the configured expires_in path, if any.
        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(auth.ExpiresInExpression))
        {
            expiresAt = ComputeExpiry(result.Body, auth.ExpiresInExpression!);
            _session.Set(scope, expiryVariable, expiresAt?.ToUnixTimeSeconds().ToString());
        }

        var acquired = _session.Get(scope, tokenVariable);
        if (string.IsNullOrEmpty(acquired))
        {
            // The most confusing success there is: a 200, and nothing to show for it. The response is
            // attached precisely so the user can look at it and see which field they should have
            // captured - which is the entire reason the capture step used to be guesswork.
            return new AuthOutcome(true, $"Token request succeeded, but no capture wrote {{{{{tokenVariable}}}}}.")
            {
                Response = new TokenResponse(result.StatusCode, result.Body),
            };
        }

        var expiryText = expiresAt is { } e ? $" (expires {e.UtcDateTime:yyyy-MM-dd HH:mm}Z)" : "";

        return new AuthOutcome(true, $"Token acquired into {{{{{tokenVariable}}}}}{expiryText}.")
        {
            Response = new TokenResponse(result.StatusCode, result.Body),
        };
    }

    // Remove every capture target plus the access-token/expiry variables from the session scope.
    private void ClearCapturedVariables(AuthConfig auth, string scope, string tokenVariable, string expiryVariable)
    {
        foreach (var capture in auth.TokenCaptures)
        {
            var name = capture.VariableName.Trim();
            if (name.Length > 0)
            {
                _session.Set(scope, name, null);
            }
        }

        _session.Set(scope, tokenVariable, null);
        _session.Set(scope, expiryVariable, null);
    }

    private static DateTimeOffset? ComputeExpiry(string body, string expression)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (node is null || !JsonPath.TryParse(expression, out var path))
        {
            return null;
        }

        if (path.Evaluate(node).Matches.FirstOrDefault()?.Value is not JsonValue value)
        {
            return null;
        }

        long seconds;
        if (value.TryGetValue<long>(out var l))
        {
            seconds = l;
        }
        else if (value.TryGetValue<double>(out var d))
        {
            seconds = (long)d;
        }
        else if (value.TryGetValue<string>(out var s) && long.TryParse(s, out var parsed))
        {
            seconds = parsed;
        }
        else
        {
            return null;
        }

        return DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    public string PreviewTokenRequest(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment)
    {
        if (auth.Type != AuthType.OAuth2)
        {
            return "Request verification is only available for OAuth 2.0.";
        }

        // Through the same upgrade the acquisition uses, so the preview shows what will ACTUALLY be
        // sent. A preview rendered by a second code path is a preview that can lie, and this one is
        // the feature's main troubleshooting tool - the one place it must not.
        return PreviewTemplateRequest(Upgraded(auth, workspace, activeEnvironment), workspace, activeEnvironment);
    }

    private string PreviewTemplateRequest(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? activeEnvironment)
    {
        string Resolve(string? value) => _resolver.Substitute(value, workspace, activeEnvironment);

        var request = auth.TokenRequest!;
        var url = Resolve(request.Url);
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Set a token request URL to preview the request.";
        }

        var method = string.IsNullOrWhiteSpace(request.Method) ? "POST" : request.Method;
        var builder = new StringBuilder();
        builder.AppendLine($"{method.ToUpperInvariant()} {url}");

        foreach (var header in request.Headers.Where(h => h.Enabled))
        {
            builder.AppendLine($"{header.Key}: {Mask(header.Key, Resolve(header.Value))}");
        }

        switch (request.Body.Type)
        {
            case BodyType.UrlEncoded:
                builder.AppendLine("Content-Type: application/x-www-form-urlencoded");
                builder.AppendLine();
                foreach (var field in request.Body.UrlEncoded.Where(f => f.Enabled))
                {
                    builder.AppendLine($"{field.Key}={Mask(field.Key, Resolve(field.Value))}");
                }

                break;

            case BodyType.Json:
            case BodyType.RawText:
                builder.AppendLine();
                builder.AppendLine(Resolve(request.Body.Raw));
                break;

            case BodyType.FormData:
                builder.AppendLine();
                foreach (var field in request.Body.FormData.Where(f => f.Enabled))
                {
                    builder.AppendLine($"{field.Key}: {Mask(field.Key, Resolve(field.Value))}");
                }

                break;
        }

        var tokenVariable = EffectiveTokenVariable(auth);
        return $"{builder.ToString().TrimEnd()}\n\n→ each request then sends:  Authorization: Bearer {{{{{tokenVariable}}}}}";
    }

    // Show config values so the user can verify them, but never echo obvious secrets.
    private static string Mask(string key, string value) =>
        key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            ? "••••••••"
            : value;

    private static string EffectiveTokenVariable(AuthConfig auth) =>
        string.IsNullOrWhiteSpace(auth.AccessTokenVariable) ? AuthDefaults.AccessTokenVariable : auth.AccessTokenVariable!;

    private static string EffectiveExpiryVariable(AuthConfig auth) =>
        string.IsNullOrWhiteSpace(auth.ExpiryVariable) ? AuthDefaults.ExpiryVariable : auth.ExpiryVariable!;

    private static bool IsExpired(string? expiryUnixSeconds)
    {
        if (string.IsNullOrEmpty(expiryUnixSeconds))
        {
            return false; // no expiry known - treat the cached token as usable
        }

        return long.TryParse(expiryUnixSeconds, out var seconds)
            && DateTimeOffset.FromUnixTimeSeconds(seconds) - ExpiryBuffer <= DateTimeOffset.UtcNow;
    }
}
