using Avalonia.Headless.XUnit;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The Run window and its view model.
///
/// Asserted on FINAL state rather than on the progress stream, and every test runs on a DISPATCHER
/// (<c>[AvaloniaFact]</c>) rather than as a plain fact. Both are load-bearing.
/// <see cref="Progress{T}"/> posts to the captured synchronization context: on the dispatcher the app
/// really has, the queued row updates run before the continuation that applies the finished report, so
/// the end state is defined. With no context at all they go to the thread pool instead and can land
/// AFTER that final apply - which made two of these tests fail intermittently before they were moved.
/// The app is never in that state, so the fix was the test, not the view model.
/// </summary>
public class CollectionRunTests
{
    private static readonly Workspace Ws = new() { RootPath = "/w", Manifest = new AppManifest { Name = "t" } };

    private static RunStep Step(int n) => new(n, $"r{n}", $"/w/collections/r{n}/request.json", "/w/collections");

    private static RunPlan Plan(int count) => new([.. Enumerable.Range(1, count).Select(Step)]);

    private CollectionRunViewModel Vm(
        FakeRunService service,
        int steps = 3,
        FakeRecording? recording = null,
        WorkspaceEnvironment? environment = null,
        params WorkspaceEnvironment[] allEnvironments) =>
        new(service, recording ?? new FakeRecording(), new FakeSnapshotStore(),
            Plan(steps), Ws, environment, allEnvironments, "Orders",
            RecordingDiffPreview, new FakeSettingsContext());

    /// <summary>Records what a row asked to open, so "the button shows the two bodies the verdict was
    /// reached from" is assertable without a window.</summary>
    private readonly FakeDiffPreview RecordingDiffPreview = new();

    private sealed class FakeDiffPreview : Fubar.Studio.UI.Services.IDiffPreviewService
    {
        public (string Left, string Right, string LeftLabel)? Shown { get; private set; }

        public Task ShowAsync(
            string leftText, string rightText, string leftLabel, string rightLabel, string title,
            Fubar.Studio.UI.Services.DiffSettingsContext? settings = null)
        {
            Shown = (leftText, rightText, leftLabel);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsContext : Fubar.Studio.UI.Services.IComparisonSettingsContext
    {
        public Task<Fubar.Studio.UI.Services.DiffSettingsContext> BuildAsync(
            Workspace workspace, string requestPath, Func<Task>? onSaved = null) =>
            Task.FromResult(new Fubar.Studio.UI.Services.DiffSettingsContext([], null, null, null));
    }

    private sealed class FakeRecording : ISnapshotRecordingService
    {
        public SnapshotRecording? Last { get; private set; }

        public Task<SnapshotRecordingReport> RecordAsync(
            SnapshotRecording recording,
            IProgress<RunProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Last = recording;

            return Task.FromResult(new SnapshotRecordingReport(
                new RunReport([], 1, false, false),
                [.. recording.Plan.Steps.Select(s => s.QualifiedName)],
                []));
        }
    }

    private sealed class FakeSnapshotStore : Fubar.Studio.Core.Snapshots.ISnapshotStore
    {
        public Task<Fubar.Studio.Core.Snapshots.SnapshotLookup> FindAsync(
            string workspaceRoot, string requestPath, string? environmentName, CancellationToken ct = default) =>
            Task.FromResult(Fubar.Studio.Core.Snapshots.SnapshotLookup.None);

        public Task SaveAsync(
            string workspaceRoot, string requestPath, Fubar.Studio.Core.Snapshots.ResponseSnapshot snapshot,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ScopesAsync(
            string workspaceRoot, string requestPath, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    // ---- The window itself ---------------------------------------------------------------------

    [AvaloniaFact]
    public void The_window_can_be_constructed()
    {
        // Deliberately asserts nothing else. A hand-written InitializeComponent overrides the generated
        // one and leaves every x:Name field null, which has already taken this codebase's process down
        // once (see CLAUDE.md). A test that merely constructs the window is what catches it.
        var window = new CollectionRunWindow(Vm(new FakeRunService()));

        Assert.NotNull(window);
    }

    [AvaloniaFact]
    public void The_windows_title_names_what_is_being_run()
    {
        // Several runs can be open at once - they are not modal - so "Run collection" on all of them
        // would make the taskbar useless.
        var window = new CollectionRunWindow(Vm(new FakeRunService()));

        Assert.Equal("Run — Orders", window.Title);
    }

    // ---- Rows before anything is sent ----------------------------------------------------------

    [AvaloniaFact]
    public void The_whole_plan_is_listed_before_the_run_starts()
    {
        // So the window can answer "how much is left?" without having finished.
        var vm = Vm(new FakeRunService(), steps: 5);

        Assert.Equal(5, vm.Steps.Count);
        Assert.All(vm.Steps, s => Assert.True(s.IsPending));
        Assert.Contains("5 requests ready", vm.Status);
    }

    [AvaloniaFact]
    public void One_request_is_not_pluralised()
    {
        Assert.Contains("1 request ready", Vm(new FakeRunService(), steps: 1).Status);
    }

    [AvaloniaFact]
    public void Filtering_rebuilds_the_rows_and_says_when_nothing_matches()
    {
        var vm = Vm(new FakeRunService(), steps: 3);

        vm.NameFilter = "r2";
        Assert.Equal(["r2"], vm.Steps.Select(s => s.Name));

        vm.NameFilter = "nothing-is-called-this";
        Assert.Empty(vm.Steps);
        Assert.Equal("Nothing matches.", vm.Status);
        Assert.False(vm.RunCommand.CanExecute(null));
    }

    // ---- After a run ---------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task A_clean_run_ends_green_with_every_row_passed()
    {
        var vm = Vm(new FakeRunService());

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.IsVerdictOk);
        Assert.False(vm.IsVerdictBad);
        Assert.All(vm.Steps, s => Assert.True(s.IsPassed));
        Assert.False(vm.IsRunning);
    }

    [AvaloniaFact]
    public async Task A_failed_assertion_shows_on_its_row_and_turns_the_verdict_red()
    {
        var vm = Vm(new FakeRunService().FailAssertionOn(2));

        await vm.RunCommand.ExecuteAsync(null);

        var row = vm.Steps.Single(s => s.Name == "r2");
        Assert.True(row.IsFailed);
        Assert.True(row.HasFailedAssertions);
        Assert.Contains("got 500", row.FailedAssertions.Single());
        Assert.True(vm.IsVerdictBad);
    }

    [AvaloniaFact]
    public async Task Only_the_failed_assertions_are_listed()
    {
        // A report's job is to point at what needs attention; the counts already say how many passed.
        var vm = Vm(new FakeRunService().FailAssertionOn(1));

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Single(vm.Steps.Single(s => s.Name == "r1").FailedAssertions);
        Assert.Contains("1/2 assertions", vm.Steps.Single(s => s.Name == "r1").Detail);
    }

    [AvaloniaFact]
    public async Task A_transport_error_shows_the_message_rather_than_a_status_code()
    {
        var vm = Vm(new FakeRunService().ErrorOn(1));

        await vm.RunCommand.ExecuteAsync(null);

        var row = vm.Steps.Single(s => s.Name == "r1");
        Assert.True(row.IsErrored);
        Assert.Equal("error", row.StatusText);
        Assert.Equal("No such host", row.Error);
    }

    [AvaloniaFact]
    public async Task A_row_the_run_never_reached_ends_as_skipped_not_stuck_on_pending()
    {
        // The progress stream never mentions these, so only applying the finished report over the rows
        // gets them off "pending" - which would otherwise read as still running, forever.
        var vm = Vm(new FakeRunService().StopAfter(1), steps: 4);
        vm.StopOnFailure = true;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Steps.Count(s => s.IsSkipped));
        Assert.DoesNotContain(vm.Steps, s => s.IsPending);
    }

    [AvaloniaFact]
    public async Task A_non_2xx_nobody_asserted_on_is_marked_but_does_not_fail_the_run()
    {
        // The bargain the whole verdict rests on: the run does not fail over a status, and the reader is
        // still told about it.
        var vm = Vm(new FakeRunService().StatusOn(2, 500).NoAssertions());

        await vm.RunCommand.ExecuteAsync(null);

        var row = vm.Steps.Single(s => s.Name == "r2");
        Assert.True(row.IsUnexpectedStatus);
        Assert.False(row.IsFailed);
        Assert.False(row.IsPassed);      // drawn as neither green nor red
        Assert.True(vm.IsVerdictOk);
    }

    [AvaloniaFact]
    public async Task Running_twice_clears_the_previous_result_first()
    {
        var service = new FakeRunService().FailAssertionOn(1);
        var vm = Vm(service);

        await vm.RunCommand.ExecuteAsync(null);
        Assert.True(vm.IsVerdictBad);

        service.FailAssertionOn(-1);     // nothing fails this time
        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.IsVerdictOk);
        Assert.All(vm.Steps, s => Assert.False(s.HasFailedAssertions));
    }

    [AvaloniaFact]
    public async Task A_run_that_cannot_even_start_is_reported_rather_than_thrown()
    {
        // Reaching here means something outside a step broke - reading the workspace's auth profiles,
        // most likely. An unobserved exception would take the window down.
        var vm = Vm(new FakeRunService().ThrowOnStart());

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.IsVerdictBad);
        Assert.Contains("could not start", vm.Summary);
        Assert.False(vm.IsRunning);
    }

    // ---- Options reach the service -------------------------------------------------------------

    [AvaloniaFact]
    public async Task The_options_the_window_shows_are_the_options_the_run_uses()
    {
        var service = new FakeRunService();
        var vm = Vm(service);
        vm.StopOnFailure = true;
        vm.RecordHistory = true;
        vm.DelayMilliseconds = 250;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(service.LastRun!.Options.StopOnFailure);
        Assert.True(service.LastRun.Options.RecordHistory);
        Assert.Equal(250, service.LastRun.Options.DelayMilliseconds);
    }

    [AvaloniaFact]
    public async Task The_filter_is_applied_to_the_plan_and_not_passed_on_as_well()
    {
        // Passing it twice would filter what is left of an already-filtered plan - the same answer only
        // by accident.
        var service = new FakeRunService();
        var vm = Vm(service, steps: 3);
        vm.NameFilter = "r3";

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(1, service.LastRun!.Plan.Count);
        Assert.Null(service.LastRun.Options.NameFilter);
    }

    // ---- Choosing what judges the run ----------------------------------------------------------

    private static readonly WorkspaceEnvironment Staging = new() { Id = "stg", Name = "Staging" };

    private static readonly WorkspaceEnvironment Production = new() { Id = "prod", Name = "Production" };

    /// <summary>Opening this window and pressing Run must never quietly start judging against
    /// something nobody chose, so the default is what Run has always done.</summary>
    [AvaloniaFact]
    public async Task Nothing_judges_the_run_unless_an_oracle_is_chosen()
    {
        var service = new FakeRunService();
        var vm = Vm(service, environment: Staging, allEnvironments: [Staging, Production]);

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(OracleKind.None, service.LastRun!.Oracle!.Kind);
    }

    [AvaloniaFact]
    public async Task Choosing_the_snapshot_oracle_reaches_the_run()
    {
        var service = new FakeRunService();
        var vm = Vm(service, environment: Staging, allEnvironments: [Staging, Production]);
        vm.SelectedOracle = vm.Oracles.Single(o => o.Kind == OracleKind.Snapshot);

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(OracleKind.Snapshot, service.LastRun!.Oracle!.Kind);

        // Nothing can be compared without them, and a run that asked for a comparison and kept no
        // bodies would report "no difference" for every step.
        Assert.True(service.LastRun.Options.CaptureResponseBodies);
    }

    /// <summary>One entry per OTHER environment: comparing an environment with itself is not a
    /// comparison, and offering it invites the click.</summary>
    [AvaloniaFact]
    public void The_environments_offered_exclude_the_one_being_run()
    {
        var vm = Vm(new FakeRunService(), environment: Staging, allEnvironments: [Staging, Production]);

        var environments = vm.Oracles.Where(o => o.Kind == OracleKind.Environment).ToList();

        Assert.Equal("Production", Assert.Single(environments).Other!.Name);
    }

    [AvaloniaFact]
    public async Task Choosing_another_environment_reaches_the_run_as_an_environment_oracle()
    {
        var service = new FakeRunService();
        var vm = Vm(service, environment: Staging, allEnvironments: [Staging, Production]);
        vm.SelectedOracle = vm.Oracles.Single(o => o.Kind == OracleKind.Environment);

        await vm.RunCommand.ExecuteAsync(null);

        var oracle = Assert.IsType<EnvironmentOracle>(service.LastRun!.Oracle);
        Assert.Equal("Production", oracle.OtherName);
    }

    // ---- Opening a difference --------------------------------------------------------------------

    /// <summary>"2 differences from Staging.json" is where the question starts, not where it ends.
    /// The two bodies opened are the ones the VERDICT was reached from - normalised, as compared -
    /// not whatever is on disk now.</summary>
    [AvaloniaFact]
    public async Task A_differing_row_opens_the_two_bodies_it_was_judged_from()
    {
        var vm = Vm(new FakeRunService().DifferingOn(2, """{"a":1}""", """{"a":2}"""));
        await vm.RunCommand.ExecuteAsync(null);

        var row = vm.Steps.Single(s => s.Name == "r2");
        Assert.True(row.CanShowDifferences);

        await vm.ShowDifferencesCommand.ExecuteAsync(row);

        var shown = RecordingDiffPreview.Shown;
        Assert.NotNull(shown);
        Assert.Equal("""{"a":1}""", shown!.Value.Left);
        Assert.Equal("""{"a":2}""", shown.Value.Right);

        // Which side it was judged against, because that is the thing a reader has to know before
        // deciding whether the difference is a regression or a stale snapshot.
        Assert.Equal("snapshots/staging.json", shown.Value.LeftLabel);
    }

    /// <summary>A row that never answered has nothing to open, and offering it would be a button that
    /// does nothing.</summary>
    [AvaloniaFact]
    public async Task A_row_with_nothing_to_compare_offers_nothing_to_open()
    {
        var vm = Vm(new FakeRunService());
        await vm.RunCommand.ExecuteAsync(null);

        Assert.All(vm.Steps, s => Assert.False(s.CanShowDifferences));
        Assert.False(vm.ShowDifferencesCommand.CanExecute(vm.Steps[0]));
    }

    // ---- Recording ------------------------------------------------------------------------------

    /// <summary>Its own button, never something Run does when it finds nothing recorded: a snapshot
    /// that writes itself on the first failing run tests nothing ever again, and does it silently.</summary>
    [AvaloniaFact]
    public async Task Recording_is_a_separate_act_from_running()
    {
        var service = new FakeRunService();
        var recording = new FakeRecording();
        var vm = Vm(service, recording: recording, environment: Staging, allEnvironments: [Staging]);

        await vm.RecordSnapshotsCommand.ExecuteAsync(null);

        Assert.NotNull(recording.Last);
        Assert.Equal("Staging", recording.Last!.Environment!.Name);
        Assert.Null(service.LastRun);
    }

    /// <summary>Per environment by default: its failure mode is a redundant file, while a shared
    /// snapshot across environments holding different data reports a data difference as a
    /// regression - and a regression tool that cries wolf stops being run.</summary>
    [AvaloniaFact]
    public async Task Recording_is_per_environment_unless_sharing_is_ticked()
    {
        var perEnvironment = new FakeRecording();
        await Vm(new FakeRunService(), recording: perEnvironment, environment: Staging)
            .RecordSnapshotsCommand.ExecuteAsync(null);

        var shared = new FakeRecording();
        var sharing = Vm(new FakeRunService(), recording: shared, environment: Staging);
        sharing.ShareSnapshots = true;
        await sharing.RecordSnapshotsCommand.ExecuteAsync(null);

        Assert.Equal(Fubar.Studio.Core.Snapshots.SnapshotScope.Environment, perEnvironment.Last!.Scope);
        Assert.Equal(Fubar.Studio.Core.Snapshots.SnapshotScope.Shared, shared.Last!.Scope);
    }

    // ---- Fake ----------------------------------------------------------------------------------

    private sealed class FakeRunService : ICollectionRunService
    {
        private readonly HashSet<int> _assertionFailures = [];
        private readonly HashSet<int> _errors = [];
        private readonly Dictionary<int, int> _statuses = [];
        private int _stopAfter = int.MaxValue;
        private bool _noAssertions;
        private bool _throwOnStart;

        public CollectionRun? LastRun { get; private set; }

        public FakeRunService FailAssertionOn(int step)
        {
            _assertionFailures.Clear();
            if (step > 0)
            {
                _assertionFailures.Add(step);
            }

            return this;
        }

        public FakeRunService ErrorOn(int step) { _errors.Add(step); return this; }

        public FakeRunService StatusOn(int step, int code) { _statuses[step] = code; return this; }

        public FakeRunService StopAfter(int count) { _stopAfter = count; return this; }

        public FakeRunService NoAssertions() { _noAssertions = true; return this; }

        public FakeRunService ThrowOnStart() { _throwOnStart = true; return this; }

        private (int Step, string Compared, string Response)? _differing;

        /// <summary>A step whose answer differs, carrying both bodies exactly as the runner does.</summary>
        public FakeRunService DifferingOn(int step, string compared, string response)
        {
            _differing = (step, compared, response);
            return this;
        }

        public Task<RunReport> RunAsync(
            CollectionRun run,
            IProgress<RunProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRun = run;

            if (_throwOnStart)
            {
                throw new IOException("auth-profiles.json is locked");
            }

            var reports = new List<StepReport>();
            foreach (var step in run.Plan.Steps)
            {
                if (reports.Count >= _stopAfter)
                {
                    reports.Add(StepReport.SkippedStep(step));
                    continue;
                }

                progress?.Report(RunProgress.Starting(step, run.Plan.Count));
                var report = Build(step);
                reports.Add(report);
                progress?.Report(RunProgress.Finished(report, run.Plan.Count));
            }

            var stoppedEarly = reports.Count > _stopAfter;
            return Task.FromResult(new RunReport(reports, 42, false, stoppedEarly));
        }

        private StepReport Build(RunStep step)
        {
            var n = step.Order;

            if (_differing is { } differing && differing.Step == n)
            {
                return new StepReport(step, StepStatus.Passed, 200, "OK", 12, 340, [], [], null)
                {
                    Comparison = ComparisonVerdict.Differs,
                    DifferenceCount = 1,
                    ComparedAgainst = "snapshots/staging.json",
                    ComparedBody = differing.Compared,
                    ResponseBody = differing.Response,
                };
            }

            if (_errors.Contains(n))
            {
                return new StepReport(step, StepStatus.Errored, null, null, 0, 0, [], [], "No such host");
            }

            IReadOnlyList<AssertionResult> assertions = _noAssertions
                ? []
                : _assertionFailures.Contains(n)
                    ? [new AssertionResult(true, "responds", "ok"), new AssertionResult(false, "status is 200", "500")]
                    : [new AssertionResult(true, "status is 200", "200")];

            var status = assertions.Any(a => !a.Passed) ? StepStatus.Failed : StepStatus.Passed;
            var code = _statuses.TryGetValue(n, out var c) ? c : 200;

            return new StepReport(step, status, code, "OK", 12, 340, assertions, [], null);
        }
    }
}
