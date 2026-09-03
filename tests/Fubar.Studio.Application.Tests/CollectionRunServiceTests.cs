using Fubar.Studio.Application.Requests;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// Running a collection: order, what stops it, and what a report says about the requests it never
/// reached.
///
/// Faked at the <see cref="IRequestExecutionService"/> seam rather than below it, because that is the
/// point of the design - a run and a single send go through the same pipeline, so the runner's own job
/// is only the walking, the stopping and the reporting.
/// </summary>
public class CollectionRunServiceTests
{
    private static readonly Workspace Ws = new() { RootPath = "/w", Manifest = new AppManifest { Name = "t" } };

    private static RunStep Step(int n) => new(n, $"r{n}", $"/w/collections/r{n}/request.json", "/w/collections");

    private static RunPlan Plan(int count) => new([.. Enumerable.Range(1, count).Select(Step)]);

    private static CollectionRun Run(RunPlan plan, RunOptions? options = null) =>
        new(plan, Ws, null, options ?? RunOptions.Default);

    private static CollectionRunService Sut(
        FakeExecution execution,
        FakeStore? store = null,
        FakeProfiles? profiles = null) =>
        new(execution, store ?? new FakeStore(), new FakeInheritance(), profiles ?? new FakeProfiles());

    // ---- Order and completeness ----------------------------------------------------------------

    [Fact]
    public async Task Requests_are_sent_in_the_plans_order()
    {
        var execution = new FakeExecution();

        await Sut(execution).RunAsync(Run(Plan(3)));

        Assert.Equal(["r1", "r2", "r3"], execution.SentNames);
    }

    [Fact]
    public async Task Every_step_appears_in_the_report()
    {
        var report = await Sut(new FakeExecution()).RunAsync(Run(Plan(3)));

        Assert.Equal(3, report.Total);
        Assert.Equal(3, report.Passed);
        Assert.True(report.Ok);
    }

    [Fact]
    public async Task An_empty_plan_runs_nothing_and_says_so()
    {
        var execution = new FakeExecution();

        var report = await Sut(execution).RunAsync(Run(RunPlan.Empty));

        Assert.Equal(0, execution.Calls);
        Assert.Equal(0, report.Total);
        Assert.False(report.Ok);
    }

    // ---- Stopping ------------------------------------------------------------------------------

    [Fact]
    public async Task By_default_a_failure_does_not_stop_the_run()
    {
        // The usual reason to run a collection is to find out what is broken; stopping at the first
        // problem answers that one request at a time.
        var execution = new FakeExecution().FailAssertionOn(1);

        var report = await Sut(execution).RunAsync(Run(Plan(3)));

        Assert.Equal(3, execution.Calls);
        Assert.Equal(1, report.Failed);
        Assert.Equal(2, report.Passed);
        Assert.Equal(0, report.Skipped);
    }

    [Fact]
    public async Task StopOnFailure_stops_at_the_first_failure()
    {
        var execution = new FakeExecution().FailAssertionOn(2);

        var report = await Sut(execution).RunAsync(Run(Plan(4), new RunOptions { StopOnFailure = true }));

        Assert.Equal(2, execution.Calls);
        Assert.True(report.StoppedEarly);
    }

    [Fact]
    public async Task Steps_the_run_never_reached_are_reported_as_skipped_not_left_out()
    {
        // A report listing 2 of 4 with no sign of the other two reads as a run of two.
        var execution = new FakeExecution().FailAssertionOn(2);

        var report = await Sut(execution).RunAsync(Run(Plan(4), new RunOptions { StopOnFailure = true }));

        Assert.Equal(4, report.Total);
        Assert.Equal(2, report.Skipped);
        Assert.Equal(
            ["r3", "r4"],
            report.Steps.Where(s => s.Status == StepStatus.Skipped).Select(s => s.Step.Name));
    }

    [Fact]
    public async Task StopOnFailure_stops_on_a_transport_error_too()
    {
        var execution = new FakeExecution().ErrorOn(2);

        var report = await Sut(execution).RunAsync(Run(Plan(4), new RunOptions { StopOnFailure = true }));

        Assert.Equal(2, execution.Calls);
        Assert.Equal(1, report.Errored);
    }

    // ---- Cancellation --------------------------------------------------------------------------

    [Fact]
    public async Task Cancelling_returns_a_report_rather_than_throwing()
    {
        using var cts = new CancellationTokenSource();
        var execution = new FakeExecution().OnSend(n => { if (n == 2) cts.Cancel(); });

        var report = await Sut(execution).RunAsync(Run(Plan(4)), progress: null, cts.Token);

        Assert.True(report.WasCancelled);
        Assert.False(report.Ok);
    }

    [Fact]
    public async Task A_request_cancelled_mid_send_is_skipped_not_errored()
    {
        // Nobody gave it a chance to answer. A red row against it would read as the request being
        // broken, and cancelling must never manufacture failures.
        using var cts = new CancellationTokenSource();
        var execution = new FakeExecution().ThrowCancelledOn(2, cts);

        var report = await Sut(execution).RunAsync(Run(Plan(3)), progress: null, cts.Token);

        Assert.Equal(StepStatus.Skipped, report.Steps.Single(s => s.Step.Name == "r2").Status);
        Assert.Equal(0, report.Errored);
    }

    // ---- One bad file does not end the run -----------------------------------------------------

    [Fact]
    public async Task A_request_file_that_will_not_load_errors_that_step_and_the_run_continues()
    {
        // Throwing would abandon the other requests over one malformed file, and hand back an exception
        // instead of the answers the run had already earned.
        var execution = new FakeExecution();
        var store = new FakeStore().FailOn("/w/collections/r2/request.json");

        var report = await Sut(execution, store).RunAsync(Run(Plan(3)));

        Assert.Equal(2, execution.Calls);
        Assert.Equal(1, report.Errored);
        Assert.Contains("Could not read the request", report.Steps.Single(s => s.Step.Name == "r2").Error);
        Assert.Equal(2, report.Passed);
    }

    // ---- Reuse of the single-send pipeline -----------------------------------------------------

    [Fact]
    public async Task Auth_profiles_are_read_once_for_the_whole_run()
    {
        // Not 200 file reads for the same answer - and a run cannot pick up an edit half way through
        // and behave differently before and after it.
        var profiles = new FakeProfiles();

        await Sut(new FakeExecution(), profiles: profiles).RunAsync(Run(Plan(5)));

        Assert.Equal(1, profiles.Loads);
    }

    [Fact]
    public async Task History_is_off_by_default_so_a_scheduled_run_does_not_evict_manual_sends()
    {
        var execution = new FakeExecution();

        await Sut(execution).RunAsync(Run(Plan(2)));

        Assert.All(execution.Runs, r => Assert.False(r.RecordHistory));
    }

    [Fact]
    public async Task History_can_be_turned_on()
    {
        var execution = new FakeExecution();

        await Sut(execution).RunAsync(Run(Plan(2), new RunOptions { RecordHistory = true }));

        Assert.All(execution.Runs, r => Assert.True(r.RecordHistory));
    }

    [Fact]
    public async Task Every_step_runs_against_the_same_workspace_and_environment()
    {
        // This is what makes captures chain: session variables are scoped per (workspace, environment),
        // so a token captured by request 1 is visible to request 2 only if both ran in the same scope.
        var environment = new WorkspaceEnvironment { Id = "dev", Name = "Dev" };
        var execution = new FakeExecution();

        await new CollectionRunService(execution, new FakeStore(), new FakeInheritance(), new FakeProfiles())
            .RunAsync(new CollectionRun(Plan(3), Ws, environment, RunOptions.Default));

        Assert.All(execution.Runs, r =>
        {
            Assert.Same(Ws, r.Workspace);
            Assert.Same(environment, r.Environment);
        });
    }

    // ---- Progress ------------------------------------------------------------------------------

    [Fact]
    public async Task Progress_reports_a_step_before_it_is_sent_as_well_as_after()
    {
        // A request that hangs is the one a reader most wants named, and a progress model that only
        // reported completions would show nothing at all for the whole time it is stuck.
        var seen = new List<string>();
        var collector = new SynchronousProgress(p => seen.Add($"{(p.IsStarting ? "start" : "done")} {p.Step.Name}"));

        await Sut(new FakeExecution()).RunAsync(Run(Plan(2)), collector);

        Assert.Equal(["start r1", "done r1", "start r2", "done r2"], seen);
    }

    [Fact]
    public async Task Progress_carries_the_total_so_a_ui_can_show_n_of_m()
    {
        var totals = new List<int>();
        var collector = new SynchronousProgress(p => totals.Add(p.Total));

        await Sut(new FakeExecution()).RunAsync(Run(Plan(3)), collector);

        Assert.All(totals, t => Assert.Equal(3, t));
    }

    // ---- Fakes ---------------------------------------------------------------------------------

    /// <summary>Collects synchronously; <see cref="Progress{T}"/> posts to the captured context, which
    /// would leave these tests racing the scheduler.</summary>
    private sealed class SynchronousProgress(Action<RunProgress> onReport) : IProgress<RunProgress>
    {
        public void Report(RunProgress value) => onReport(value);
    }

    private sealed class FakeExecution : IRequestExecutionService
    {
        private readonly HashSet<int> _assertionFailures = [];
        private readonly HashSet<int> _errors = [];
        private Action<int>? _onSend;
        private (int Step, CancellationTokenSource Source)? _cancelOn;

        public int Calls { get; private set; }

        public List<RequestRun> Runs { get; } = [];

        public List<string> SentNames { get; } = [];

        public FakeExecution FailAssertionOn(int step) { _assertionFailures.Add(step); return this; }

        public FakeExecution ErrorOn(int step) { _errors.Add(step); return this; }

        public FakeExecution OnSend(Action<int> action) { _onSend = action; return this; }

        public FakeExecution ThrowCancelledOn(int step, CancellationTokenSource source)
        {
            _cancelOn = (step, source);
            return this;
        }

        public Task<RequestRunResult> RunAsync(RequestRun run, CancellationToken cancellationToken = default)
        {
            Calls++;
            Runs.Add(run);
            SentNames.Add(run.Request.Name);
            var n = int.Parse(run.Request.Name[1..]);
            _onSend?.Invoke(n);

            if (_cancelOn is { } cancel && cancel.Step == n)
            {
                cancel.Source.Cancel();
                throw new OperationCanceledException();
            }

            if (_errors.Contains(n))
            {
                return Task.FromResult(new RequestRunResult(
                    new ExecutionResult { ErrorMessage = "No such host" }, null, [], [], null, null));
            }

            IReadOnlyList<AssertionResult> assertions = _assertionFailures.Contains(n)
                ? [new AssertionResult(false, "status is 200", "500")]
                : [new AssertionResult(true, "status is 200", "200")];

            return Task.FromResult(new RequestRunResult(
                new ExecutionResult { StatusCode = 200, ReasonPhrase = "OK" }, null, assertions, [], null, null));
        }
    }

    private sealed class FakeStore : IRequestStore
    {
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

    private sealed class FakeInheritance : IInheritanceResolver
    {
        public Task<InheritanceChain> GetInheritanceChainAsync(string root, string requestFilePath, CancellationToken ct = default) =>
            Task.FromResult(new InheritanceChain([], null, null, Array.Empty<ComparisonSettingsLayer>()));
    }

    private sealed class FakeProfiles : IAuthProfileStore
    {
        public int Loads { get; private set; }

        public Task<IReadOnlyList<AuthProfile>> LoadAuthProfilesAsync(string root, CancellationToken ct = default)
        {
            Loads++;
            return Task.FromResult<IReadOnlyList<AuthProfile>>([]);
        }

        public Task SaveAuthProfilesAsync(string rootPath, IReadOnlyList<AuthProfile> profiles, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
