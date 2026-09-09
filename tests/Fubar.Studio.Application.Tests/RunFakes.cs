using Fubar.Studio.Core.Snapshots;
using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Tests;

// The roles a run touches but does not exercise: it only ever READS a request, resolves an empty
// inheritance chain, and loads the workspace's auth profiles once. Shared by every runner test so
// the single-environment and paired runs are measured against exactly the same doubles.
internal sealed class FakeStore : IRequestStore
{
    /// <summary>No migration happens in a fake store, so nothing ever raises this.</summary>
    public event Action<string, IReadOnlyList<string>>? RequestMigrated { add { } remove { } }
    private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);

    public FakeStore FailOn(string path) { _failing.Add(path); return this; }

    public Task<RequestModel> LoadRequestAsync(string path, CancellationToken ct = default)
    {
        if (_failing.Contains(path))
        {
            throw new InvalidDataException("unexpected token");
        }

        // The folder name is the request name: /w/collections/r2/request.json -> r2
        var name = Path.GetFileName(Path.GetDirectoryName(path))!;
        return Task.FromResult(new RequestModel { Name = name, Url = "https://example.test/" });
    }

    // The rest of the role. A run only ever READS a request, so anything the runner calls here is a
    // bug rather than something to give a plausible answer to.
    public Task SaveRequestAsync(string path, RequestModel request, CancellationToken ct = default) => throw new NotSupportedException();

    public IReadOnlyList<WorkspaceTreeNode> BuildCollectionsTree(string rootPath) => throw new NotSupportedException();

    public string CreateRequest(string parentDirectory, string requestName) => throw new NotSupportedException();

    public string CreateFolder(string parentDirectory, string folderName) => throw new NotSupportedException();

    public string DuplicatePath(string path) => throw new NotSupportedException();

    public string RenamePath(string path, string newName) => throw new NotSupportedException();

    public void DeletePath(string path) => throw new NotSupportedException();
}

internal sealed class FakeInheritance : IInheritanceResolver
{
    public Task<InheritanceChain> GetInheritanceChainAsync(string root, string requestFilePath, CancellationToken ct = default) =>
        Task.FromResult(new InheritanceChain([], null, null, Array.Empty<ComparisonSettingsLayer>()));
}

internal sealed class FakeProfiles : IAuthProfileStore
{
    public int Loads { get; private set; }

    public Task<IReadOnlyList<AuthProfile>> LoadAuthProfilesAsync(string root, CancellationToken ct = default)
    {
        Loads++;
        return Task.FromResult<IReadOnlyList<AuthProfile>>([]);
    }

    public Task SaveAuthProfilesAsync(string rootPath, IReadOnlyList<AuthProfile> profiles, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>
/// A comparer that answers from a table of prepared verdicts rather than running an engine. The
/// runner's job is to ASK and to record what it hears; whether the engine is right is
/// ResponseComparerTests' business.
/// </summary>
internal sealed class FakeComparer : IResponseComparer
{
    private readonly Func<string, string, ComparisonOutcome> _answer;

    public FakeComparer(Func<string, string, ComparisonOutcome>? answer = null) =>
        _answer = answer ?? ((l, r) => string.Equals(l, r, StringComparison.Ordinal)
            ? ComparisonOutcome.Identical
            : new ComparisonOutcome(1, true, []));

    public List<(string Left, string Right)> Compared { get; } = [];

    public Task<ComparisonOutcome> CompareAsync(
        string left, string right, ResolvedComparisonSettings settings, CancellationToken ct = default)
    {
        Compared.Add((left, right));
        return Task.FromResult(_answer(left, right));
    }
}

/// <summary>Cases from a dictionary rather than a directory. Empty for every test that is about the
/// requests format, where a step never names one.</summary>
internal sealed class FakeEndpoints : IEndpointStore
{
    private readonly Dictionary<string, EndpointCase> _cases = new(StringComparer.OrdinalIgnoreCase);

    public FakeEndpoints With(string path, EndpointCase endpointCase)
    {
        _cases[path] = endpointCase;
        return this;
    }

    public bool IsEndpoint(string directory) => false;

    public string? EndpointDirectoryOf(string path) => null;

    public IReadOnlyList<CaseSummary> ListCases(string endpointDirectory) =>
        [.. _cases.Select(c => new CaseSummary(c.Value.Name, c.Key))];

    public Task<EndpointCase> LoadCaseAsync(string caseFilePath, CancellationToken ct = default) =>
        _cases.TryGetValue(caseFilePath, out var found)
            ? Task.FromResult(found)
            : throw new FileNotFoundException(caseFilePath);

    public Task SaveCaseAsync(string caseFilePath, EndpointCase endpointCase, CancellationToken ct = default)
    {
        _cases[caseFilePath] = endpointCase;
        return Task.CompletedTask;
    }

    public string CreateCase(string endpointDirectory, string caseName) => throw new NotSupportedException();

    public string ProposeCasePath(string endpointDirectory, string caseName) =>
        throw new NotSupportedException();

    public string RenameCase(string caseFilePath, string newName) => throw new NotSupportedException();

    public string CreateEndpoint(string parentDirectory, string endpointName) => throw new NotSupportedException();
}

/// <summary>No rules at any level, which is what most runner tests want to say.</summary>
internal sealed class FakeComparisonSettings : IRequestComparisonSettings
{
    /// <summary>Tolerances every request gets, for the tests that are about forgiving a difference.</summary>
    public List<ResolvedTolerance> Tolerances { get; } = [];

    public FakeComparisonSettings Tolerating(Tolerance tolerance)
    {
        Tolerances.Add(new ResolvedTolerance(tolerance, ComparisonScope.Request, "Request"));
        return this;
    }

    public Task<ResolvedRequestRules> ResolveRulesAsync(
        Workspace workspace, string requestPath, string? casePath = null,
        BatchOverlay? overlay = null, CancellationToken ct = default) =>
        Task.FromResult(new ResolvedRequestRules(
            ComparisonSettingsResolver.Resolve([]), Tolerances, ResolvedSnapshotPolicy.Empty));
}

/// <summary>
/// The execution seam every runner test fakes at: a run and a single send go through the same
/// pipeline, so the runner's own job is only the walking, the judging and the reporting.
///
/// <para>Records what it was sent as "name@environment", which is what makes the interleaving
/// assertions readable.</para>
/// </summary>
internal sealed class FakeExecution : IRequestExecutionService
{
    private readonly Dictionary<string, int> _statuses = [];
    private readonly HashSet<string> _errors = [];
    private string? _body;
    private bool _bodyPerEnvironment;
    private (string Key, CancellationTokenSource Source)? _cancelOn;

    public List<string> Sent { get; } = [];

    public List<WorkspaceEnvironment?> Environments { get; } = [];

    public FakeExecution Body(string body) { _body = body; return this; }

    public FakeExecution BodyPerEnvironment() { _bodyPerEnvironment = true; return this; }

    public FakeExecution StatusFor(string environmentId, int status) { _statuses[environmentId] = status; return this; }

    public FakeExecution ErrorOn(string environmentId) { _errors.Add(environmentId); return this; }

    private readonly HashSet<string> _erroringSteps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fails one named request rather than a whole environment - what a teardown test needs,
    /// since its cleanup step runs against the same environment as everything else.</summary>
    public FakeExecution ErrorOnStep(string requestName) { _erroringSteps.Add(requestName); return this; }

    private readonly HashSet<string> _failingAssertions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Answers, but with a failed assertion - which is what a cleanup step reusing a test's
    /// case looks like when the thing it deletes has already gone.</summary>
    public FakeExecution AssertionFailureOn(string requestName) { _failingAssertions.Add(requestName); return this; }

    public FakeExecution CancelOn(string key, CancellationTokenSource source)
    {
        _cancelOn = (key, source);
        return this;
    }

    public IReadOnlyList<string> SentTo(string environmentId) =>
        [.. Sent.Where(s => s.EndsWith($"@{environmentId}", StringComparison.Ordinal))
                .Select(s => s[..s.IndexOf('@', StringComparison.Ordinal)])];

    public Task<RequestRunResult> RunAsync(RequestRun run, CancellationToken cancellationToken = default)
    {
        var env = run.Environment?.Id ?? "none";
        var key = $"{run.Request.Name}@{env}";
        Sent.Add(key);
        Environments.Add(run.Environment);

        if (_cancelOn is { } cancel && cancel.Key == key)
        {
            cancel.Source.Cancel();
            throw new OperationCanceledException();
        }

        if (_errors.Contains(env) || _erroringSteps.Contains(run.Request.Name))
        {
            return Task.FromResult(new RequestRunResult(
                new ExecutionResult { ErrorMessage = "No such host" }, null, [], [], null, null));
        }

        var body = _bodyPerEnvironment ? $$"""{"env":"{{env}}"}""" : _body ?? "";

        return Task.FromResult(new RequestRunResult(
            new ExecutionResult
            {
                StatusCode = _statuses.TryGetValue(env, out var status) ? status : 200,
                ReasonPhrase = "OK",
                Body = body,
                ContentType = "application/json",
            },
            null,
            _failingAssertions.Contains(run.Request.Name)
                ? [new AssertionResult(false, "status is 204", "404")]
                : [new AssertionResult(true, "status is 200", "200")],
            [],
            null,
            null));
    }
}
