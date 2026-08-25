using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Testing;

namespace Fubar.Studio.Application.Requests;

/// <inheritdoc cref="IRequestExecutionService"/>
public sealed class RequestExecutionService : IRequestExecutionService
{
    private readonly IAuthProvider _authProvider;
    private readonly IExecutorRegistry _executorRegistry;
    private readonly IResponseTestService _testService;
    private readonly IHistoryService _historyService;

    public RequestExecutionService(
        IAuthProvider authProvider,
        IExecutorRegistry executorRegistry,
        IResponseTestService testService,
        IHistoryService historyService)
    {
        _authProvider = authProvider;
        _executorRegistry = executorRegistry;
        _testService = testService;
        _historyService = historyService;
    }

    public async Task<RequestRunResult> RunAsync(RequestRun run, CancellationToken cancellationToken = default)
    {
        var executor = _executorRegistry.Resolve(run.Request.Kind);

        // 1. Auth prestep: acquire (OAuth2) + apply. The applied credential (headers/query) is injected into
        //    a clone used only for execution, so resolved tokens never reach history.
        AuthOutcome? auth = null;
        var requestToExecute = run.Request;
        IReadOnlyList<string>? sensitiveHeaderNames = null;
        // Only OAuth2 has an "acquire" step worth re-running on a 401.
        var hasAcquireStep = run.EffectiveAuth is { Type: AuthType.OAuth2 };
        if (run.EffectiveAuth is { } effectiveAuth)
        {
            var prep = await _authProvider.PrepareAsync(effectiveAuth, run.Workspace, run.Environment, forceReacquire: false, cancellationToken);
            auth = prep.Outcome;
            requestToExecute = AuthRequestMerge.Inject(run.Request, prep.Applied);
            // Names of the injected credential headers, so the executor drops them on a cross-origin redirect.
            sensitiveHeaderNames = prep.Applied.Headers.Select(h => h.Key).ToList();
        }

        // 2. Execute via whichever protocol executor the request's kind resolves to.
        var context = new RequestExecutionContext(run.Workspace, run.Environment, sensitiveHeaderNames);
        var result = await executor.ExecuteAsync(requestToExecute, context, cancellationToken);

        // 2b. Retry once on 401 for acquire-based schemes: force a re-acquire and resend (a stale/expired
        //     token the cache still considered valid).
        if (result.StatusCode == 401 && hasAcquireStep && run.EffectiveAuth is { } retryAuth)
        {
            var prep = await _authProvider.PrepareAsync(retryAuth, run.Workspace, run.Environment, forceReacquire: true, cancellationToken);
            auth = prep.Outcome;
            var retried = AuthRequestMerge.Inject(run.Request, prep.Applied);
            result = await executor.ExecuteAsync(retried, context, cancellationToken);
        }

        // 3. On a real response (not a transport error), apply captures then evaluate assertions.
        IReadOnlyList<CaptureResult> captures = [];
        IReadOnlyList<AssertionResult> assertions = [];
        if (result.IsSuccess)
        {
            if (run.Request.Captures.Count > 0)
            {
                captures = await _testService.ApplyCapturesAsync(run.Request.Captures, result, run.Workspace, run.Environment, cancellationToken);
            }

            if (run.Request.Assertions.Count > 0)
            {
                assertions = _testService.RunAssertions(run.Request.Assertions, result);
            }
        }

        // 4. Record history (a failed/error send is still useful history). A persistence failure is
        //    surfaced but never fails the run.
        ExecutionSnapshot? snapshot = null;
        string? historyError = null;
        if (run.RecordHistory)
        {
            var candidate = BuildSnapshot(run.Request, result);
            try
            {
                await _historyService.AppendAsync(run.Workspace.RootPath, run.Request.Id, candidate, cancellationToken);
                snapshot = candidate;
            }
            catch (Exception ex)
            {
                historyError = ex.Message;
            }
        }

        return new RequestRunResult(result, auth, assertions, captures, snapshot, historyError);
    }

    private static ExecutionSnapshot BuildSnapshot(RequestModel request, ExecutionResult result) => new()
    {
        Method = request.Method,
        Url = request.Url,
        Headers = request.Headers.Where(h => h.Enabled).ToList(),
        Body = request.Body.Raw,
        StatusCode = result.StatusCode,
        ReasonPhrase = result.ReasonPhrase,
        ElapsedMilliseconds = result.ElapsedMilliseconds,
        SizeBytes = result.SizeBytes,
        ResponseBody = HistoryBodyPolicy.Capture(result.Body),
        ErrorMessage = result.ErrorMessage,
    };
}
