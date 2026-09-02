using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Auth;
using Fubar.Studio.Infrastructure.Testing;
using Fubar.Studio.Infrastructure.Variables;

namespace Fubar.Studio.Infrastructure.Tests.Auth;

public class OAuthTests
{
    private sealed class NoSecrets : ISecretStoreService
    {
        public string? TryGetSecret(string workspaceId, string key) => null;
        public void SetSecret(string workspaceId, string key, string value) { }
        public void DeleteSecret(string workspaceId, string key) { }
    }

    private sealed class NoEnvStore : IEnvironmentStore
    {
        public Task<IReadOnlyList<WorkspaceEnvironment>> LoadEnvironmentsAsync(string rootPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceEnvironment>>([]);
        public Task SaveEnvironmentAsync(string rootPath, WorkspaceEnvironment environment, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteEnvironmentAsync(string rootPath, string environmentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>A stub HTTP executor for the template auth path: returns a canned result and records the
    /// request it was asked to run.</summary>
    private sealed class StubExecutor : IRequestExecutor
    {
        public RequestKind Kind => RequestKind.Http;
        public ExecutionResult Result { get; set; } = new() { StatusCode = 200, Body = "{}" };
        public RequestModel? LastRequest { get; private set; }
        public int Calls { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(RequestModel request, RequestExecutionContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubExecutorRegistry(IRequestExecutor executor) : IExecutorRegistry
    {
        public IRequestExecutor Resolve(RequestKind kind) => executor;
    }

    private static Workspace Workspace => new() { RootPath = "C:/fake", Manifest = new AppManifest { Id = "ws1", Name = "T" } };

    // Session state is scoped per (workspace, environment); these tests use the null-environment scope.
    private static string Scope => SessionScope.For(Workspace, (WorkspaceEnvironment?)null);
    private static AuthProvider MakeProvider(
        SessionVariableStore session,
        IExecutorRegistry? registry = null,
        IResponseTestService? testService = null) =>
        new(new VariableResolver(new NoSecrets(), session),
            session,
            registry ?? new StubExecutorRegistry(new StubExecutor()),
            testService ?? new ResponseTestService(session, new NoEnvStore()));

    [Fact]
    public async Task Provider_AcquiresAndStoresTokenInSessionVariables()
    {
        // A LEGACY config - no TokenRequest. It is upgraded and run down the one path, so the token
        // arrives through the executor rather than through the old token service.
        var session = new SessionVariableStore();
        var executor = new StubExecutor
        {
            Result = new ExecutionResult { StatusCode = 200, Body = """{ "access_token": "token", "expires_in": 3600 }""" },
        };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));
        var auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c", ClientSecret = "s" };

        var outcome = (await provider.PrepareAsync(auth, Workspace, null)).Outcome;

        Assert.True(outcome.Ok);
        Assert.Equal("token", session.Get(Scope, AuthDefaults.AccessTokenVariable));
        Assert.False(string.IsNullOrEmpty(session.Get(Scope, AuthDefaults.ExpiryVariable)));
        Assert.Equal(1, executor.Calls);
    }

    [Fact]
    public async Task Provider_ReusesCachedToken_WhenNotExpired()
    {
        var session = new SessionVariableStore();
        var executor = new StubExecutor
        {
            Result = new ExecutionResult { StatusCode = 200, Body = """{ "access_token": "token", "expires_in": 3600 }""" },
        };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));
        var auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c" };

        await provider.PrepareAsync(auth, Workspace, null);
        await provider.PrepareAsync(auth, Workspace, null);

        Assert.Equal(1, executor.Calls); // second call reused the cached, unexpired token
    }

    [Fact]
    public async Task Provider_Reacquires_WhenCachedTokenExpired()
    {
        var session = new SessionVariableStore();
        var executor = new StubExecutor
        {
            // expires_in of -10 puts the token in the past the moment it is stored.
            Result = new ExecutionResult { StatusCode = 200, Body = """{ "access_token": "token", "expires_in": -10 }""" },
        };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));
        var auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c" };

        await provider.PrepareAsync(auth, Workspace, null); // stores an already-expired token
        await provider.PrepareAsync(auth, Workspace, null); // must re-acquire

        Assert.Equal(2, executor.Calls);
    }

    [Fact]
    public void Provider_PreviewTokenRequest_ResolvesVariables_AndNeedsTokenUrl()
    {
        var session = new SessionVariableStore();
        var provider = MakeProvider(session);

        var missingUrl = provider.PreviewTokenRequest(new AuthConfig { Type = AuthType.OAuth2 }, Workspace, null);
        Assert.Contains("token request URL", missingUrl);

        // A LEGACY config previewed through the one path - which is the point of the collapse: the
        // preview is the feature's main troubleshooting tool, and one rendered by a second code path
        // is a preview that can disagree with what actually gets sent.
        var ok = provider.PreviewTokenRequest(
            new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c" }, Workspace, null);
        Assert.Contains("POST https://auth/token", ok);
        Assert.Contains("Authorization: Bearer {{oauth2_access_token}}", ok);
    }

    [Fact]
    public async Task Provider_NoOp_ForNonOAuth2()
    {
        var provider = MakeProvider(new SessionVariableStore());

        var prep = await provider.PrepareAsync(new AuthConfig { Type = AuthType.Bearer, Token = "abc" }, Workspace, null);

        Assert.True(prep.Outcome.Ok);
        Assert.Equal("Bearer abc", Assert.Single(prep.Applied.Headers).Value);
    }

    [Fact]
    public async Task Provider_Template_AppliesAcquiredTokenAsBearerHeader()
    {
        var session = new SessionVariableStore();
        var executor = new StubExecutor { Result = new ExecutionResult { StatusCode = 200, Body = """{ "access_token": "tkn" }""" } };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));

        var prep = await provider.PrepareAsync(TemplateAuth(), Workspace, null);

        var header = Assert.Single(prep.Applied.Headers);
        Assert.Equal("Authorization", header.Key);
        Assert.Equal("Bearer tkn", header.Value);
    }

    [Fact]
    public async Task Provider_ForceReacquire_BypassesCachedToken()
    {
        var session = new SessionVariableStore();
        var executor = new StubExecutor
        {
            Result = new ExecutionResult { StatusCode = 200, Body = """{ "access_token": "token", "expires_in": 3600 }""" },
        };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));
        var auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c" };

        await provider.PrepareAsync(auth, Workspace, null);
        await provider.PrepareAsync(auth, Workspace, null, forceReacquire: true);

        Assert.Equal(2, executor.Calls); // the forced re-acquire ignored the cached, unexpired token
    }

    [Fact]
    public async Task A_legacy_config_asking_for_Basic_client_auth_still_sends_Basic()
    {
        // The one thing the collapse could silently have broken. The legacy engine built the header
        // itself; the upgrade puts credentials in the form body by default, so a config that asked for
        // HTTP Basic would quietly have started sending them the other way - which some servers reject
        // and none of them explain well.
        var session = new SessionVariableStore();
        var executor = new StubExecutor
        {
            Result = new ExecutionResult { StatusCode = 200, Body = """{ "access_token": "t" }""" },
        };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));

        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            TokenUrl = "https://auth/token",
            ClientId = "cid",
            ClientSecret = "csec",
            ClientAuthentication = OAuth2ClientAuth.BasicHeader,
        };

        await provider.PrepareAsync(auth, Workspace, null);

        var header = Assert.Single(executor.LastRequest!.Headers, h => h.Key == "Authorization");

        Assert.Equal("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("cid:csec")), header.Value);

        // And the credentials are NOT also in the body, which would be sending them twice.
        Assert.DoesNotContain(executor.LastRequest.Body.UrlEncoded, f => f.Key == "client_secret");
    }

    // --- Template path ------------------------------------------------------------------------------

    private static AuthConfig TemplateAuth(IEnumerable<CaptureRule>? captures = null) => new()
    {
        Type = AuthType.OAuth2,
        TokenRequest = new AuthTokenRequest { Method = "POST", Url = "https://auth/token", Body = new RequestBody { Type = BodyType.UrlEncoded } },
        TokenCaptures = (captures ?? [new CaptureRule { VariableName = AuthDefaults.AccessTokenVariable, Expression = "$.access_token" }]).ToList(),
        ExpiresInExpression = "$.expires_in",
    };

    [Fact]
    public async Task Provider_Template_RunsRequest_CapturesToken_And_ComputesExpiry()
    {
        var session = new SessionVariableStore();
        var executor = new StubExecutor
        {
            Result = new ExecutionResult { StatusCode = 200, Body = """{ "access_token": "tkn", "expires_in": 3600 }""" },
        };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));

        var outcome = (await provider.PrepareAsync(TemplateAuth(), Workspace, null)).Outcome;

        Assert.True(outcome.Ok);
        Assert.Equal(1, executor.Calls);
        Assert.Equal("POST", executor.LastRequest?.Method);
        Assert.Equal("https://auth/token", executor.LastRequest?.Url);
        Assert.Equal("tkn", session.Get(Scope, AuthDefaults.AccessTokenVariable));
        Assert.False(string.IsNullOrEmpty(session.Get(Scope, AuthDefaults.ExpiryVariable)));
    }

    [Fact]
    public async Task Provider_Template_ClearsCapturedVariables_OnFailure()
    {
        var session = new SessionVariableStore();
        // Pre-seed as if a prior success happened (expiry is in the past so the cache doesn't short-circuit).
        session.Set(Scope, AuthDefaults.AccessTokenVariable, "stale");
        session.Set(Scope, AuthDefaults.ExpiryVariable, "123");
        session.Set(Scope, "extra_token", "old");

        var executor = new StubExecutor { Result = new ExecutionResult { StatusCode = 401, Body = "unauthorized" } };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));

        var captures = new[]
        {
            new CaptureRule { VariableName = AuthDefaults.AccessTokenVariable, Expression = "$.access_token" },
            new CaptureRule { VariableName = "extra_token", Expression = "$.other" },
        };

        var outcome = (await provider.PrepareAsync(TemplateAuth(captures), Workspace, null)).Outcome;

        Assert.False(outcome.Ok);
        Assert.Null(session.Get(Scope, AuthDefaults.AccessTokenVariable));
        Assert.Null(session.Get(Scope, AuthDefaults.ExpiryVariable));
        Assert.Null(session.Get(Scope, "extra_token"));
    }

    [Fact]
    public async Task Provider_Template_ClearsCapturedVariables_OnTransportError()
    {
        var session = new SessionVariableStore();
        session.Set(Scope, AuthDefaults.AccessTokenVariable, "stale");
        session.Set(Scope, AuthDefaults.ExpiryVariable, "123"); // already expired, so the cache doesn't short-circuit

        var executor = new StubExecutor { Result = new ExecutionResult { ErrorMessage = "connection refused", ElapsedMilliseconds = 1 } };
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));

        var outcome = (await provider.PrepareAsync(TemplateAuth(), Workspace, null)).Outcome;

        Assert.False(outcome.Ok);
        Assert.Null(session.Get(Scope, AuthDefaults.AccessTokenVariable));
    }

    [Fact]
    public async Task Provider_Template_ReusesCachedToken_WhenNotExpired()
    {
        var session = new SessionVariableStore();
        session.Set(Scope, AuthDefaults.AccessTokenVariable, "cached");
        session.Set(Scope, AuthDefaults.ExpiryVariable, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString());

        var executor = new StubExecutor();
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));

        var outcome = (await provider.PrepareAsync(TemplateAuth(), Workspace, null)).Outcome;

        Assert.True(outcome.Ok);
        Assert.Equal(0, executor.Calls); // the cached, unexpired token was reused - no request sent
    }

    [Fact]
    public void Apply_ResolvesBearer_WithoutAcquiring()
    {
        var provider = MakeProvider(new SessionVariableStore());

        var applied = provider.Apply(new AuthConfig { Type = AuthType.Bearer, Token = "abc" }, Workspace, null);

        Assert.Equal("Bearer abc", Assert.Single(applied.Headers).Value);

        // Apply never acquires - that is PrepareAsync's job.
    }

    [Fact]
    public void Apply_OAuth2_UsesTokenAlreadyInSession()
    {
        var session = new SessionVariableStore();
        session.Set(Scope, AuthDefaults.AccessTokenVariable, "cached-tok");
        var provider = MakeProvider(session);

        var applied = provider.Apply(new AuthConfig { Type = AuthType.OAuth2 }, Workspace, null);

        Assert.Equal("Bearer cached-tok", Assert.Single(applied.Headers).Value);
    }

    // ---- Unresolved variables --------------------------------------------------------------------

    [Fact]
    public async Task An_unresolved_variable_in_the_token_url_is_named_rather_than_requested()
    {
        // Substitution leaves what it cannot resolve exactly as it found it, so this used to travel
        // into the token request as the literal "{{authHost}}/token" and come back as an invalid-URI
        // error - a symptom in a different place from the cause, naming the wrong thing entirely.
        var session = new SessionVariableStore();
        var provider = MakeProvider(session);

        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            TokenUrl = "{{authHost}}/token",
            ClientId = "c",
            ClientSecret = "s",
        };

        var outcome = (await provider.PrepareAsync(auth, Workspace, null)).Outcome;

        Assert.False(outcome.Ok);
        Assert.Contains("{{authHost}}", outcome.Message);

    }

    [Fact]
    public async Task Every_unresolved_field_is_named_at_once()
    {
        // One trip round the loop per missing variable is the slow way to configure OAuth.
        var session = new SessionVariableStore();
        var provider = MakeProvider(session);

        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            TokenUrl = "{{authHost}}/token",
            ClientId = "{{clientId}}",
            ClientSecret = "s",
        };

        var outcome = (await provider.PrepareAsync(auth, Workspace, null)).Outcome;

        Assert.Contains("{{authHost}}", outcome.Message);
        Assert.Contains("{{clientId}}", outcome.Message);
    }

    [Fact]
    public async Task The_TEMPLATE_path_names_unresolved_variables_too()
    {
        // The path that actually matters, and the one the first version of this guard missed: the
        // editor writes a TokenRequest, so this is where nearly every user is. The three tests above
        // all set TokenUrl with no TokenRequest, which is the LEGACY path - they passed while the
        // real path was unguarded.
        var session = new SessionVariableStore();
        var executor = new StubExecutor();
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));

        var auth = TemplateAuth();
        auth.TokenRequest!.Url = "{{token_url}}/token";

        var outcome = (await provider.PrepareAsync(auth, Workspace, null)).Outcome;

        Assert.False(outcome.Ok);
        Assert.Contains("{{token_url}}", outcome.Message);

        // And nothing was sent. Handing a literal "{{token_url}}/token" to the executor is what
        // produced the malformed-URI error that named everything except the cause.
        Assert.Equal(0, executor.Calls);
    }

    [Fact]
    public async Task The_TEMPLATE_path_reads_headers_and_body_for_variables()
    {
        var session = new SessionVariableStore();
        var executor = new StubExecutor();
        var provider = MakeProvider(session, new StubExecutorRegistry(executor));

        var auth = TemplateAuth();
        auth.TokenRequest!.Body = new RequestBody
        {
            Type = BodyType.UrlEncoded,
            UrlEncoded = [new KeyValueItem { Key = "client_id", Value = "{{client_id}}" }],
        };

        var outcome = (await provider.PrepareAsync(auth, Workspace, null)).Outcome;

        Assert.Contains("{{client_id}}", outcome.Message);
        Assert.Equal(0, executor.Calls);
    }

    [Fact]
    public async Task A_variable_that_DOES_resolve_is_not_reported()
    {
        // The guard must not fire on the ordinary case, which is the entire point of using variables -
        // and resolving from the ENVIRONMENT is the case that was broken from the profile editor.
        var session = new SessionVariableStore();
        var provider = MakeProvider(session);

        var environment = new WorkspaceEnvironment
        {
            Id = "e1",
            Name = "Dev",
            Variables = [new AppVariable { Key = "authHost", Value = "https://auth.example.com" }],
        };

        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            TokenUrl = "{{authHost}}/token",
            ClientId = "c",
            ClientSecret = "s",
        };

        var outcome = (await provider.PrepareAsync(auth, Workspace, environment)).Outcome;

        Assert.True(outcome.Ok);
    }
}
