using Fubar.Studio.Application.Requests;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// Running one collection against two environments and pairing the answers.
///
/// The interleaving is the part worth pinning: left, right, next request - not one whole environment
/// then the other. It is what lets a comparison be written against request 1 while request 12 is still
/// running, and it is only safe because everything an environment accumulates is keyed by it.
/// </summary>
public class EnvironmentPairRunServiceTests
{
    private static readonly Workspace Ws = new() { RootPath = "/w", Manifest = new AppManifest { Name = "t" } };

    private static readonly WorkspaceEnvironment Staging = new() { Id = "stg", Name = "Staging" };
    private static readonly WorkspaceEnvironment Prod = new() { Id = "prd", Name = "Production" };

    private static RunStep Step(int n) => new(n, $"r{n}", $"/w/collections/r{n}/request.json", "/w/collections");

    private static RunPlan Plan(int count) => new([.. Enumerable.Range(1, count).Select(Step)]);

    private static EnvironmentPairRun Run(RunPlan plan, RunOptions? options = null) =>
        new(plan, Ws, Staging, Prod, options ?? RunOptions.Default);

    private static CollectionRunService Sut(FakeExecution execution) =>
        new(execution, new FakeStore(), new FakeInheritance(), new FakeProfiles());

    // ---- Interleaving ---------------------------------------------------------------------------

    [Fact]
    public async Task Each_request_is_sent_to_both_sides_before_the_next_one_starts()
    {
        var execution = new FakeExecution();

        await Sut(execution).RunAsync(Run(Plan(3)));

        Assert.Equal(
            ["r1@stg", "r1@prd", "r2@stg", "r2@prd", "r3@stg", "r3@prd"],
            execution.Sent);
    }

    /// <summary>Captures chain, so a request must still follow the one before it WITHIN its own
    /// environment. Interleaving is only allowed to reorder across environments, never inside one.</summary>
    [Fact]
    public async Task Each_side_still_runs_in_plan_order()
    {
        var execution = new FakeExecution();

        await Sut(execution).RunAsync(Run(Plan(3)));

        Assert.Equal(["r1", "r2", "r3"], execution.SentTo("stg"));
        Assert.Equal(["r1", "r2", "r3"], execution.SentTo("prd"));
    }

    [Fact]
    public async Task Each_side_is_sent_its_own_environment()
    {
        var execution = new FakeExecution();

        await Sut(execution).RunAsync(Run(Plan(1)));

        Assert.Equal([Staging, Prod], execution.Environments);
    }

    // ---- What a pair carries --------------------------------------------------------------------

    [Fact]
    public async Task A_pair_carries_both_answers_and_the_environment_names()
    {
        var execution = new FakeExecution().BodyPerEnvironment();

        var report = await Sut(execution).RunAsync(Run(Plan(2)));

        Assert.Equal("Staging", report.LeftEnvironment);
        Assert.Equal("Production", report.RightEnvironment);
        Assert.Equal(2, report.Total);
        Assert.Equal("""{"env":"stg"}""", report.Pairs[0].Left.ResponseBody);
        Assert.Equal("""{"env":"prd"}""", report.Pairs[0].Right.ResponseBody);
    }

    /// <summary>Bodies are the entire point of a comparison run, so it asks for them whatever the
    /// caller set - a run that faithfully honoured CaptureResponseBodies=false would have nothing to
    /// compare and no way to say why.</summary>
    [Fact]
    public async Task Bodies_are_kept_even_when_the_options_did_not_ask_for_them()
    {
        var execution = new FakeExecution().BodyPerEnvironment();

        var report = await Sut(execution).RunAsync(
            Run(Plan(1), RunOptions.Default with { CaptureResponseBodies = false }));

        Assert.NotNull(report.Pairs[0].Left.ResponseBody);
        Assert.NotNull(report.Pairs[0].Right.ResponseBody);
    }

    [Fact]
    public async Task Identical_bodies_are_recognised_without_comparing_them()
    {
        var execution = new FakeExecution().Body("""{"same":true}""");

        var report = await Sut(execution).RunAsync(Run(Plan(1)));

        Assert.True(report.Pairs[0].BodiesIdentical);
        Assert.False(report.Pairs[0].StatusDiffers);
        Assert.False(report.Pairs[0].NotComparable);
    }

    /// <summary>Different text is NOT an answer on its own - key order or an ignored field could still
    /// make the two equal, and only the comparison engine can say. The pair reports what it knows.</summary>
    [Fact]
    public async Task Differing_bodies_are_left_for_the_comparison_to_judge()
    {
        var execution = new FakeExecution().BodyPerEnvironment();

        var report = await Sut(execution).RunAsync(Run(Plan(1)));

        Assert.False(report.Pairs[0].BodiesIdentical);
        Assert.False(report.Pairs[0].NotComparable);
    }

    [Fact]
    public async Task A_status_mismatch_is_reported_on_its_own()
    {
        var execution = new FakeExecution().StatusFor("prd", 503);

        var report = await Sut(execution).RunAsync(Run(Plan(2)));

        Assert.Equal(2, report.StatusMismatches.Count);
        Assert.True(report.Pairs[0].StatusDiffers);
    }

    /// <summary>Over the cap the body is dropped and says so, rather than truncated: two bodies cut at
    /// the same length compare as identical past the cut.</summary>
    [Fact]
    public async Task A_body_too_large_to_compare_is_dropped_not_truncated()
    {
        var execution = new FakeExecution().Body(new string('x', StepReport.MaxComparableBodyChars + 1));

        var report = await Sut(execution).RunAsync(Run(Plan(1)));

        Assert.Null(report.Pairs[0].Left.ResponseBody);
        Assert.True(report.Pairs[0].Left.BodyTooLargeToCompare);
        Assert.True(report.Pairs[0].NotComparable);
    }

    [Fact]
    public async Task An_errored_side_carries_no_body_and_is_not_comparable()
    {
        var execution = new FakeExecution().Body("""{"ok":true}""").ErrorOn("prd");

        var report = await Sut(execution).RunAsync(Run(Plan(1)));

        Assert.True(report.Pairs[0].NotComparable);
        Assert.False(report.Pairs[0].Left.BodyTooLargeToCompare);
    }

    // ---- Stopping -------------------------------------------------------------------------------

    [Fact]
    public async Task Every_step_appears_even_when_the_run_stops_early()
    {
        var execution = new FakeExecution().ErrorOn("prd");

        var report = await Sut(execution).RunAsync(
            Run(Plan(4), RunOptions.Default with { StopOnFailure = true }));

        Assert.Equal(4, report.Total);
        Assert.True(report.StoppedEarly);
        Assert.Equal(StepStatus.Skipped, report.Pairs[3].Left.Status);
    }

    /// <summary>A pair half-run is not a comparison. Reporting the side that did answer would put a row
    /// on screen inviting a diff against nothing.</summary>
    [Fact]
    public async Task Cancelling_between_the_two_sides_reports_neither()
    {
        using var source = new CancellationTokenSource();
        var execution = new FakeExecution().CancelOn("r2@prd", source);

        var report = await Sut(execution).RunAsync(Run(Plan(3)), null, source.Token);

        Assert.True(report.WasCancelled);
        Assert.Equal(StepStatus.Skipped, report.Pairs[1].Left.Status);
        Assert.Equal(StepStatus.Skipped, report.Pairs[1].Right.Status);
    }

    [Fact]
    public async Task An_empty_plan_still_names_both_environments()
    {
        var report = await Sut(new FakeExecution()).RunAsync(Run(RunPlan.Empty));

        Assert.Equal(0, report.Total);
        Assert.Equal("Staging", report.LeftEnvironment);
        Assert.Equal("Production", report.RightEnvironment);
    }

    // ---- Progress -------------------------------------------------------------------------------

    /// <summary>The left side is reported the moment it lands, so the UI can show one column filling in
    /// while the other is still in flight - and the pair is reported complete separately, because that
    /// is the point at which a comparison becomes possible.</summary>
    [Fact]
    public async Task Progress_reports_the_left_side_before_the_pair_is_complete()
    {
        var seen = new List<string>();
        var progress = new Progress<StepPairProgress>(p => seen.Add(
            p.Pair is not null ? $"{p.Step.Name}:pair" : p.Left is not null ? $"{p.Step.Name}:left" : $"{p.Step.Name}:start"));

        await Sut(new FakeExecution()).RunAsync(Run(Plan(1)), progress);

        // Progress<T> posts through the synchronization context, so give it a turn to drain.
        await Task.Delay(50);

        Assert.Equal(["r1:start", "r1:left", "r1:pair"], seen);
    }

    // ---- Fakes ----------------------------------------------------------------------------------

    private sealed class FakeExecution : IRequestExecutionService
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

            if (_errors.Contains(env))
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
                [new AssertionResult(true, "status is 200", "200")],
                [],
                null,
                null));
        }
    }
}
