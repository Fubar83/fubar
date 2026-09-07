using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Application.Requests;

/// <inheritdoc cref="IRequestExecutionService"/>
public sealed class RequestExecutionService : IRequestExecutionService
{
    private readonly IAuthProvider _authProvider;
    private readonly IExecutorRegistry _executorRegistry;
    private readonly IResponseTestService _testService;
    private readonly IHistoryService _historyService;
    private readonly IVariableResolver _variableResolver;
    private readonly IAppSettingsService _settings;

    public RequestExecutionService(
        IAuthProvider authProvider,
        IExecutorRegistry executorRegistry,
        IResponseTestService testService,
        IHistoryService historyService,
        IVariableResolver variableResolver,
        IAppSettingsService settings)
    {
        _authProvider = authProvider;
        _executorRegistry = executorRegistry;
        _testService = testService;
        _historyService = historyService;
        _variableResolver = variableResolver;
        _settings = settings;
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

        // 1b. Refuse to send a placeholder.
        //
        // Substitute leaves what it cannot resolve exactly as it found it, so an undefined {{token}}
        // travels to the server as those nine literal characters. The guard for this already existed
        // and was applied to exactly one caller - the OAuth token request - where its own comment
        // explains why it matters. The ordinary send took the same risk unguarded, which is worse: it
        // is the path that carries credentials, and a header of "Bearer {{api_key}}" comes back as a
        // 401 that says nothing about a variable.
        var context = new RequestExecutionContext(run.Workspace, run.Environment, sensitiveHeaderNames);

        if (DescribeUnresolved(requestToExecute, run) is { } unresolved)
        {
            return new RequestRunResult(
                new ExecutionResult { ErrorMessage = unresolved },
                auth,
                [],
                [],
                HistorySnapshot: null,
                HistoryError: null);
        }

        // 2. Execute via whichever protocol executor the request's kind resolves to.
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
        // The user setting is checked here as well as the caller's flag: a run can ask for no history,
        // and someone who has turned history off entirely must get none whatever any caller asks for.
        if (run.RecordHistory && _settings.Load().History.Enabled)
        {
            var candidate = BuildSnapshot(run.Request, result, _settings.Load().History.MaxResponseBodyKilobytes * 1024);
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

    /// <summary>
    /// Names the variables that would still be <c>{{tokens}}</c> when this request reached the wire, or
    /// null when everything resolves.
    ///
    /// <para>Substitution happens inside the executor, so the check has to substitute too - testing the
    /// raw request would flag every request that merely USES a variable. Only the parts that actually
    /// travel are examined, and only enabled ones: a disabled header carrying an old placeholder is not
    /// a reason to refuse a send.</para>
    /// </summary>
    private string? DescribeUnresolved(RequestModel request, RequestRun run)
    {
        string Resolve(string? text) => _variableResolver.Substitute(text, run.Workspace, run.Environment);

        var texts = new List<string?> { Resolve(request.Url) };

        texts.AddRange(request.Headers.Where(h => h.Enabled).Select(h => Resolve(h.Value)));
        texts.AddRange(request.QueryParams.Where(p => p.Enabled).Select(p => Resolve(p.Value)));
        texts.Add(Resolve(request.Body.Raw));
        texts.AddRange(request.Body.FormData.Where(f => f.Enabled).Select(f => Resolve(f.Value)));
        texts.AddRange(request.Body.UrlEncoded.Where(f => f.Enabled).Select(f => Resolve(f.Value)));

        return UnresolvedVariables.Describe(UnresolvedVariables.In([.. texts]));
    }

    private static ExecutionSnapshot BuildSnapshot(RequestModel request, ExecutionResult result, int maxBodyChars) => new()
    {
        Method = request.Method,
        Url = request.Url,
        Headers = request.Headers.Where(h => h.Enabled).ToList(),
        Body = request.Body.Raw,
        StatusCode = result.StatusCode,
        ReasonPhrase = result.ReasonPhrase,
        ElapsedMilliseconds = result.ElapsedMilliseconds,
        SizeBytes = result.SizeBytes,
        ResponseBody = HistoryBodyPolicy.Capture(result.Body, maxBodyChars),
        ErrorMessage = result.ErrorMessage,
    };
}
