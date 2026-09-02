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
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "{}";
        public string? LastBody { get; private set; }
        public AuthenticationHeaderValueSnapshot? LastAuthHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.Headers.Authorization is { } auth)
            {
                LastAuthHeader = new AuthenticationHeaderValueSnapshot(auth.Scheme, auth.Parameter);
            }

            return new HttpResponseMessage(Status) { Content = new StringContent(ResponseBody) };
        }
    }

    public sealed record AuthenticationHeaderValueSnapshot(string Scheme, string? Parameter);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

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

    [Fact]
    public async Task TokenService_ClientCredentials_SendsGrantAndParsesResponse()
    {
        var handler = new StubHandler { ResponseBody = """{ "access_token": "abc", "expires_in": 3600, "refresh_token": "r1" }""" };
        var service = new OAuthTokenService(new StubHttpClientFactory(handler));

        var result = await service.AcquireAsync(new OAuth2TokenRequest(
            OAuth2GrantType.ClientCredentials, "https://auth/token", "cid", "csec", "read write", null, OAuth2ClientAuth.Body));

        Assert.Equal("abc", result.AccessToken);
        Assert.Equal("r1", result.RefreshToken);
        Assert.NotNull(result.ExpiresAt);
        Assert.Contains("grant_type=client_credentials", handler.LastBody);
        Assert.Contains("client_id=cid", handler.LastBody);
        Assert.Contains("scope=read", handler.LastBody);
    }

    [Fact]
    public async Task TokenService_BasicHeaderAuth_SendsAuthorizationHeader_NotBodyCredentials()
    {
        var handler = new StubHandler { ResponseBody = """{ "access_token": "abc" }""" };
        var service = new OAuthTokenService(new StubHttpClientFactory(handler));

        await service.AcquireAsync(new OAuth2TokenRequest(
            OAuth2GrantType.ClientCredentials, "https://auth/token", "cid", "csec", null, null, OAuth2ClientAuth.BasicHeader));

        Assert.Equal("Basic", handler.LastAuthHeader?.Scheme);
        Assert.DoesNotContain("client_id", handler.LastBody);
    }

    [Fact]
    public async Task TokenService_Refresh_SendsRefreshGrant()
    {
        var handler = new StubHandler { ResponseBody = """{ "access_token": "new" }""" };
        var service = new OAuthTokenService(new StubHttpClientFactory(handler));

        await service.AcquireAsync(new OAuth2TokenRequest(
            OAuth2GrantType.RefreshToken, "https://auth/token", "cid", null, null, "the-refresh-token", OAuth2ClientAuth.Body));

        Assert.Contains("grant_type=refresh_token", handler.LastBody);
        Assert.Contains("refresh_token=the-refresh-token", handler.LastBody);
    }

    [Fact]
    public async Task TokenService_NonSuccess_Throws()
    {
        var handler = new StubHandler { Status = HttpStatusCode.BadRequest, ResponseBody = """{ "error": "invalid_client" }""" };
        var service = new OAuthTokenService(new StubHttpClientFactory(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcquireAsync(
            new OAuth2TokenRequest(OAuth2GrantType.ClientCredentials, "https://auth/token", "cid", "bad", null, null, OAuth2ClientAuth.Body)));
    }

    private sealed class CountingTokenService : IOAuthTokenService
    {
        public int Calls { get; private set; }
        public OAuthTokenResult Result { get; set; } = new("token", DateTimeOffset.UtcNow.AddHours(1), null);

        public Task<OAuthTokenResult> AcquireAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }

        public string Describe(OAuth2TokenRequest request) => $"POST {request.TokenUrl}";
    }

    private static AuthProvider MakeProvider(
        IOAuthTokenService tokenService,
        SessionVariableStore session,
        IExecutorRegistry? registry = null,
        IResponseTestService? testService = null) =>
        new(tokenService,
            new VariableResolver(new NoSecrets(), session),
            session,
            registry ?? new StubExecutorRegistry(new StubExecutor()),
            testService ?? new ResponseTestService(session, new NoEnvStore()));

    [Fact]
    public async Task Provider_AcquiresAndStoresTokenInSessionVariables()
    {
        var session = new SessionVariableStore();
        var provider = MakeProvider(new CountingTokenService(), session);
        var auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c", ClientSecret = "s" };

        var outcome = (await provider.PrepareAsync(auth, Workspace, null)).Outcome;

        Assert.True(outcome.Ok);
        Assert.Equal("token", session.Get(Scope, AuthDefaults.AccessTokenVariable));
        Assert.False(string.IsNullOrEmpty(session.Get(Scope, AuthDefaults.ExpiryVariable)));
    }

    [Fact]
    public async Task Provider_ReusesCachedToken_WhenNotExpired()
    {
        var session = new SessionVariableStore();
        var token = new CountingTokenService();
        var provider = MakeProvider(token, session);
        var auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c" };

        await provider.PrepareAsync(auth, Workspace, null);
        await provider.PrepareAsync(auth, Workspace, null);

        Assert.Equal(1, token.Calls); // second call reused the cached, unexpired token
    }

    [Fact]
    public async Task Provider_Reacquires_WhenCachedTokenExpired()
    {
        var session = new SessionVariableStore();
        var token = new CountingTokenService { Result = new("token", DateTimeOffset.UtcNow.AddSeconds(-10), null) };
        var provider = MakeProvider(token, session);
        var auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c" };

        await provider.PrepareAsync(auth, Workspace, null); // stores an already-expired token
        await provider.PrepareAsync(auth, Workspace, null); // must re-acquire

        Assert.Equal(2, token.Calls);
    }

    [Fact]
    public void TokenService_Describe_ShowsRequest_MasksSecrets()
    {
        var service = new OAuthTokenService(new StubHttpClientFactory(new StubHandler()));
        var preview = service.Describe(new OAuth2TokenRequest(
            OAuth2GrantType.ClientCredentials, "https://auth/token", "my-client", "s3cret", "read", null, OAuth2ClientAuth.Body));

        Assert.Contains("POST https://auth/token", preview);
        Assert.Contains("grant_type=client_credentials", preview);
        Assert.Contains("client_id=my-client", preview); // non-secret shown
        Assert.Contains("scope=read", preview);
        Assert.DoesNotContain("s3cret", preview);         // secret masked
    }

    [Fact]
    public void Provider_PreviewTokenRequest_ResolvesVariables_AndNeedsTokenUrl()
    {
        var session = new SessionVariableStore();
        var provider = MakeProvider(new CountingTokenService(), session);

        var missingUrl = provider.PreviewTokenRequest(new AuthConfig { Type = AuthType.OAuth2 }, Workspace, null);
        Assert.Contains("token URL", missingUrl);

        var ok = provider.PreviewTokenRequest(
            new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c" }, Workspace, null);
        Assert.Contains("POST https://auth/token", ok);
        Assert.Contains("Authorization: Bearer {{oauth2_access_token}}", ok);
    }

    [Fact]
    public async Task Provider_NoOp_ForNonOAuth2()
    {
        var token = new CountingTokenService();
        var provider = MakeProvider(token, new SessionVariableStore());

        var prep = await provider.PrepareAsync(new AuthConfig { Type = AuthType.Bearer, Token = "abc" }, Workspace, null);

        Assert.True(prep.Outcome.Ok);
        Assert.Equal(0, token.Calls); // static scheme - no token acquisition
        Assert.Equal("Bearer abc", Assert.Single(prep.Applied.Headers).Value);
    }

    [Fact]
    public async Task Provider_Template_AppliesAcquiredTokenAsBearerHeader()
    {
        var session = new SessionVariableStore();
        var executor = new StubExecutor { Result = new ExecutionResult { StatusCode = 200, Body = """{ "access_token": "tkn" }""" } };
        var provider = MakeProvider(new CountingTokenService(), session, new StubExecutorRegistry(executor));

        var prep = await provider.PrepareAsync(TemplateAuth(), Workspace, null);

        var header = Assert.Single(prep.Applied.Headers);
        Assert.Equal("Authorization", header.Key);
        Assert.Equal("Bearer tkn", header.Value);
    }

    [Fact]
    public async Task Provider_ForceReacquire_BypassesCachedToken()
    {
        var session = new SessionVariableStore();
        var token = new CountingTokenService();
        var provider = MakeProvider(token, session);
        var auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://auth/token", ClientId = "c" };

        await provider.PrepareAsync(auth, Workspace, null);
        await provider.PrepareAsync(auth, Workspace, null, forceReacquire: true);

        Assert.Equal(2, token.Calls); // the forced re-acquire ignored the cached, unexpired token
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
        var provider = MakeProvider(new CountingTokenService(), session, new StubExecutorRegistry(executor));

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
        var provider = MakeProvider(new CountingTokenService(), session, new StubExecutorRegistry(executor));

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
        var provider = MakeProvider(new CountingTokenService(), session, new StubExecutorRegistry(executor));

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
        var provider = MakeProvider(new CountingTokenService(), session, new StubExecutorRegistry(executor));

        var outcome = (await provider.PrepareAsync(TemplateAuth(), Workspace, null)).Outcome;

        Assert.True(outcome.Ok);
        Assert.Equal(0, executor.Calls); // the cached, unexpired token was reused - no request sent
    }

    [Fact]
    public void Apply_ResolvesBearer_WithoutAcquiring()
    {
        var token = new CountingTokenService();
        var provider = MakeProvider(token, new SessionVariableStore());

        var applied = provider.Apply(new AuthConfig { Type = AuthType.Bearer, Token = "abc" }, Workspace, null);

        Assert.Equal("Bearer abc", Assert.Single(applied.Headers).Value);
        Assert.Equal(0, token.Calls); // Apply never triggers a token request
    }

    [Fact]
    public void Apply_OAuth2_UsesTokenAlreadyInSession()
    {
        var session = new SessionVariableStore();
        session.Set(Scope, AuthDefaults.AccessTokenVariable, "cached-tok");
        var provider = MakeProvider(new CountingTokenService(), session);

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
        var tokenService = new CountingTokenService();
        var provider = MakeProvider(tokenService, session);

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

        // And no request was made. Sending one that cannot succeed is how the confusing error got
        // produced in the first place.
        Assert.Equal(0, tokenService.Calls);
    }

    [Fact]
    public async Task Every_unresolved_field_is_named_at_once()
    {
        // One trip round the loop per missing variable is the slow way to configure OAuth.
        var session = new SessionVariableStore();
        var provider = MakeProvider(new CountingTokenService(), session);

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
    public async Task A_variable_that_DOES_resolve_is_not_reported()
    {
        // The guard must not fire on the ordinary case, which is the entire point of using variables -
        // and resolving from the ENVIRONMENT is the case that was broken from the profile editor.
        var session = new SessionVariableStore();
        var provider = MakeProvider(new CountingTokenService(), session);

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
