using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Testing;

namespace Fubar.Studio.Application.Tests;

public class RequestExecutionServiceTests
{
    private static readonly Workspace Ws = new() { RootPath = "root", Manifest = new AppManifest { Name = "t" } };

    private static RequestModel RequestWithTests() => new()
    {
        Name = "r",
        Assertions = [new Assertion { Source = ResponseField.StatusCode, Operator = AssertionOperator.Equals, Expected = "200" }],
        Captures = [new CaptureRule { VariableName = "id", Source = ResponseField.JsonBody, Expression = "$.id" }],
    };

    [Fact]
    public async Task On_success_runs_captures_assertions_and_records_history()
    {
        var executor = new FakeExecutorRegistry(new ExecutionResult { StatusCode = 200, Body = "{\"id\":1}" });
        var tests = new FakeTestService();
        var history = new FakeHistoryService();
        var sut = new RequestExecutionService(new FakeAuthProvider(), executor, tests, history);

        var result = await sut.RunAsync(new RequestRun(RequestWithTests(), Ws, null, EffectiveAuth: null));

        Assert.True(tests.CapturesApplied);
        Assert.True(tests.AssertionsRun);
        Assert.NotNull(result.HistorySnapshot);
        Assert.Equal(1, history.AppendCount);
        Assert.Null(result.HistoryError);
    }

    [Fact]
    public async Task On_transport_error_skips_tests_but_still_records_history()
    {
        var executor = new FakeExecutorRegistry(new ExecutionResult { ErrorMessage = "boom" });
        var tests = new FakeTestService();
        var history = new FakeHistoryService();
        var sut = new RequestExecutionService(new FakeAuthProvider(), executor, tests, history);

        var result = await sut.RunAsync(new RequestRun(RequestWithTests(), Ws, null, EffectiveAuth: null));

        Assert.False(tests.CapturesApplied);
        Assert.False(tests.AssertionsRun);
        Assert.NotNull(result.HistorySnapshot); // error sends are still history
        Assert.Equal(1, history.AppendCount);
    }

    [Fact]
    public async Task Ensures_auth_only_when_effective_auth_is_supplied()
    {
        var auth = new FakeAuthProvider();
        var sut = new RequestExecutionService(auth, new FakeExecutorRegistry(new ExecutionResult { StatusCode = 200 }), new FakeTestService(), new FakeHistoryService());

        await sut.RunAsync(new RequestRun(new RequestModel { Name = "r" }, Ws, null, EffectiveAuth: null));
        Assert.Equal(0, auth.PrepareCount);

        await sut.RunAsync(new RequestRun(new RequestModel { Name = "r" }, Ws, null, new AuthConfig { Type = AuthType.Bearer }));
        Assert.Equal(1, auth.PrepareCount);
    }

    [Fact]
    public async Task Injects_applied_auth_into_the_executed_request()
    {
        var executor = new FakeExecutorRegistry(new ExecutionResult { StatusCode = 200 });
        var auth = new FakeAuthProvider { Applied = new AppliedAuth([new KeyValueItem { Key = "Authorization", Value = "Bearer tok" }], []) };
        var sut = new RequestExecutionService(auth, executor, new FakeTestService(), new FakeHistoryService());

        await sut.RunAsync(new RequestRun(new RequestModel { Name = "r" }, Ws, null, new AuthConfig { Type = AuthType.Bearer }));

        Assert.Contains(executor.LastRequest!.Headers, h => h.Key == "Authorization" && h.Value == "Bearer tok");
    }

    [Fact]
    public async Task Retries_once_on_401_for_oauth2_forcing_a_reacquire()
    {
        var executor = new FakeExecutorRegistry(new ExecutionResult { StatusCode = 401 }, new ExecutionResult { StatusCode = 200 });
        var auth = new FakeAuthProvider();
        var sut = new RequestExecutionService(auth, executor, new FakeTestService(), new FakeHistoryService());

        var result = await sut.RunAsync(new RequestRun(new RequestModel { Name = "r" }, Ws, null, new AuthConfig { Type = AuthType.OAuth2 }));

        Assert.Equal(200, result.Result.StatusCode); // the retry succeeded
        Assert.Equal(2, executor.Calls);
        Assert.Equal(1, auth.ForceReacquireCount);
    }

    [Fact]
    public async Task Does_not_retry_on_401_for_static_schemes()
    {
        var executor = new FakeExecutorRegistry(new ExecutionResult { StatusCode = 401 });
        var auth = new FakeAuthProvider();
        var sut = new RequestExecutionService(auth, executor, new FakeTestService(), new FakeHistoryService());

        await sut.RunAsync(new RequestRun(new RequestModel { Name = "r" }, Ws, null, new AuthConfig { Type = AuthType.Bearer }));

        Assert.Equal(1, executor.Calls); // no acquire step, so no retry
        Assert.Equal(0, auth.ForceReacquireCount);
    }

    [Fact]
    public async Task History_persistence_failure_is_surfaced_not_thrown()
    {
        var sut = new RequestExecutionService(new FakeAuthProvider(), new FakeExecutorRegistry(new ExecutionResult { StatusCode = 200 }), new FakeTestService(), new ThrowingHistoryService());

        var result = await sut.RunAsync(new RequestRun(new RequestModel { Name = "r" }, Ws, null, null));

        Assert.Null(result.HistorySnapshot);
        Assert.Equal("disk full", result.HistoryError);
    }

    // --- fakes -------------------------------------------------------------------------------------

    private sealed class FakeAuthProvider : IAuthProvider
    {
        public int PrepareCount { get; private set; }

        public int ForceReacquireCount { get; private set; }

        public AppliedAuth Applied { get; set; } = AppliedAuth.Empty;

        public Task<AuthPreparation> PrepareAsync(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? env, bool forceReacquire = false, CancellationToken ct = default)
        {
            PrepareCount++;
            if (forceReacquire)
            {
                ForceReacquireCount++;
            }

            return Task.FromResult(new AuthPreparation(Applied, new AuthOutcome(true, "")));
        }

        public AppliedAuth Apply(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? env) => Applied;

        public string PreviewTokenRequest(AuthConfig auth, Workspace workspace, WorkspaceEnvironment? env) => "";
    }

    private sealed class FakeExecutorRegistry(params ExecutionResult[] results) : IExecutorRegistry, IRequestExecutor
    {
        private readonly Queue<ExecutionResult> _results = new(results);

        public RequestModel? LastRequest { get; private set; }

        public int Calls { get; private set; }

        public RequestKind Kind => RequestKind.Http;

        public IRequestExecutor Resolve(RequestKind kind) => this;

        public Task<ExecutionResult> ExecuteAsync(RequestModel request, RequestExecutionContext context, CancellationToken ct = default)
        {
            Calls++;
            LastRequest = request;
            // Return each queued result in order, then keep returning the last one.
            return Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
        }
    }

    private sealed class FakeTestService : IResponseTestService
    {
        public bool AssertionsRun { get; private set; }

        public bool CapturesApplied { get; private set; }

        public IReadOnlyList<AssertionResult> RunAssertions(IReadOnlyList<Assertion> assertions, ExecutionResult result)
        {
            AssertionsRun = true;
            return [new AssertionResult(true, "ok", "200")];
        }

        public Task<IReadOnlyList<CaptureResult>> ApplyCapturesAsync(IReadOnlyList<CaptureRule> captures, ExecutionResult result, Workspace workspace, WorkspaceEnvironment? env, CancellationToken ct = default)
        {
            CapturesApplied = true;
            return Task.FromResult<IReadOnlyList<CaptureResult>>([new CaptureResult(true, "id", "1", "session", null)]);
        }
    }

    private sealed class FakeHistoryService : IHistoryService
    {
        public int AppendCount { get; private set; }

        public Task<IReadOnlyList<ExecutionSnapshot>> LoadAsync(string root, string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionSnapshot>>([]);

        public Task AppendAsync(string root, string id, ExecutionSnapshot snapshot, CancellationToken ct = default)
        {
            AppendCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHistoryService : IHistoryService
    {
        public Task<IReadOnlyList<ExecutionSnapshot>> LoadAsync(string root, string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionSnapshot>>([]);

        public Task AppendAsync(string root, string id, ExecutionSnapshot snapshot, CancellationToken ct = default) =>
            throw new IOException("disk full");
    }
}
