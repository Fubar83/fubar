using System.Xml.Linq;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.Cli;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The command-line run: which exit code comes back, and what is said on the way.
///
/// The exit codes are the whole contract — 0 passed, 1 failed, 2 could not tell — and the third is kept
/// strictly apart from the second because a workspace that would not load and a collection whose
/// assertions failed call for completely different reactions from a build.
/// </summary>
public class CliRunnerTests
{
    private const int Passed = 0;
    private const int Failed = 1;
    private const int CouldNotRun = 2;

    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    private string Output => _out.ToString();

    private string Error => _error.ToString();

    private Task<int> Run(
        string[] args,
        FakeRunService? runService = null,
        FakeWorkspaces? workspaces = null,
        FakeRequests? requests = null,
        FakeEnvironments? environments = null,
        FakeSnapshotRecording? recording = null,
        FakeBatches? batches = null) =>
        CliRunner.RunAsync(
            CommandLine.Parse(args),
            new CliServices(
                runService ?? new FakeRunService(),
                workspaces ?? new FakeWorkspaces(),
                requests ?? new FakeRequests(),
                environments ?? new FakeEnvironments(),
                new FakeSnapshots(),
                recording ?? new FakeSnapshotRecording(),
                batches ?? new FakeBatches()),
            _out,
            _error);

    // ---- Help and version ----------------------------------------------------------------------

    [Fact]
    public async Task Help_prints_usage_and_succeeds()
    {
        Assert.Equal(Passed, await Run(["--help"]));
        Assert.Contains("--run", Output);
    }

    [Fact]
    public async Task A_parse_error_is_explained_and_exits_could_not_run()
    {
        // Exit 2, not 1: nothing was tested, so reporting a test failure would be a lie.
        Assert.Equal(CouldNotRun, await Run(["--run", "--nope"]));
        Assert.Contains("--nope", Error);
    }

    // ---- Verdicts ------------------------------------------------------------------------------

    [Fact]
    public async Task A_clean_run_exits_zero()
    {
        Assert.Equal(Passed, await Run(["--run", "-w", FakeWorkspaces.Root], new FakeRunService()));
    }

    [Fact]
    public async Task A_failed_assertion_exits_one()
    {
        Assert.Equal(Failed, await Run(["--run", "-w", FakeWorkspaces.Root], new FakeRunService().FailOn(2)));
    }

    [Fact]
    public async Task A_transport_error_exits_one()
    {
        Assert.Equal(Failed, await Run(["--run", "-w", FakeWorkspaces.Root], new FakeRunService().ErrorOn(1)));
    }

    [Fact]
    public async Task A_non_2xx_nobody_asserted_on_still_exits_zero_but_is_said_out_loud()
    {
        // The bargain the whole feature rests on, and the one place a script author could be surprised -
        // so the note is printed rather than left to the report file.
        var exit = await Run(["--run", "-w", FakeWorkspaces.Root], new FakeRunService().UnexpectedStatusOn(2, 503));

        Assert.Equal(Passed, exit);
        Assert.Contains("note:", Output);
        Assert.Contains("503", Output);
    }

    // ---- Nothing to run ------------------------------------------------------------------------

    [Fact]
    public async Task A_filter_matching_nothing_exits_one_not_zero()
    {
        // "Nothing matched, so it passed" is the failure mode every test runner has had to grow out of,
        // and one typo in --filter reaches it.
        var exit = await Run(["--run", "-w", FakeWorkspaces.Root, "--filter", "zzz"]);

        Assert.Equal(Failed, exit);
        Assert.Contains("Nothing to run", Error);
    }

    [Fact]
    public async Task A_bare_run_names_the_workspace_rather_than_printing_empty_quotes()
    {
        await Run(["--run", "-w", FakeWorkspaces.Root, "--filter", "zzz"]);

        Assert.Contains("the workspace", Error);
        Assert.DoesNotContain("\"\"", Error);
    }

    // ---- Could not run -------------------------------------------------------------------------

    [Fact]
    public async Task A_workspace_that_is_not_one_exits_could_not_run()
    {
        var exit = await Run(["--run", "-w", "/not/a/workspace"], workspaces: new FakeWorkspaces());

        Assert.Equal(CouldNotRun, exit);
        Assert.Contains("not a workspace", Error);
    }

    [Fact]
    public async Task An_environment_that_does_not_exist_is_an_error_and_lists_the_ones_that_do()
    {
        // Never a quiet fall back to none: every {{variable}} would resolve to nothing and the run would
        // fail in a way that pointed at the requests rather than at the typo.
        var exit = await Run(
            ["--run", "-w", FakeWorkspaces.Root, "--env", "Stagng"],
            environments: new FakeEnvironments("Staging", "Production"));

        Assert.Equal(CouldNotRun, exit);
        Assert.Contains("Staging", Error);
        Assert.Contains("Production", Error);
    }

    [Fact]
    public async Task A_named_environment_that_exists_is_used()
    {
        var runService = new FakeRunService();

        await Run(
            ["--run", "-w", FakeWorkspaces.Root, "--env", "Staging"],
            runService,
            environments: new FakeEnvironments("Staging"));

        Assert.Equal("Staging", runService.LastRun!.Environment!.Name);
    }

    [Fact]
    public async Task A_run_target_that_is_not_in_the_workspace_exits_could_not_run()
    {
        var exit = await Run(["--run", "Nope", "-w", FakeWorkspaces.Root]);

        Assert.Equal(CouldNotRun, exit);
        Assert.Contains("not a folder, endpoint or request", Error);
    }

    [Fact]
    public async Task A_request_is_found_without_typing_its_json_extension()
    {
        // Requests are stored as <name>.json and nobody types that.
        var runService = new FakeRunService();

        var exit = await Run(["--run", "Orders/Create", "-w", FakeWorkspaces.Root], runService);

        Assert.Equal(Passed, exit);
        Assert.Equal(["Create"], runService.LastRun!.Plan.Steps.Select(s => s.Name));
    }

    // ---- Options reach the run -----------------------------------------------------------------

    [Fact]
    public async Task History_is_never_recorded_by_a_command_line_run()
    {
        // History is a record of what a PERSON sent; a CI run writing entries every build would evict
        // exactly that, and there is deliberately no flag to turn it on.
        var runService = new FakeRunService();

        await Run(["--run", "-w", FakeWorkspaces.Root], runService);

        Assert.False(runService.LastRun!.Options.RecordHistory);
    }

    [Fact]
    public async Task Stop_on_failure_and_delay_are_passed_through()
    {
        var runService = new FakeRunService();

        await Run(["--run", "-w", FakeWorkspaces.Root, "--stop-on-failure", "--delay", "250"], runService);

        Assert.True(runService.LastRun!.Options.StopOnFailure);
        Assert.Equal(250, runService.LastRun.Options.DelayMilliseconds);
    }

    [Fact]
    public async Task Quiet_says_nothing_at_all()
    {
        // What -q means to grep and diff: the exit code is the answer.
        var exit = await Run(["--run", "-w", FakeWorkspaces.Root, "-q"], new FakeRunService().FailOn(1));

        Assert.Equal(Failed, exit);
        Assert.Equal("", Output);
    }

    [Fact]
    public async Task Without_quiet_each_request_is_reported_as_it_lands()
    {
        await Run(["--run", "-w", FakeWorkspaces.Root], new FakeRunService().FailOn(2));

        Assert.Contains("ok", Output);
        Assert.Contains("FAIL", Output);
        Assert.Contains("passed", Output);   // the summary line
    }

    // ---- Reports -------------------------------------------------------------------------------

    [Fact]
    public async Task A_report_is_written_in_the_format_the_extension_implies()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fubar-cli-{Guid.NewGuid():n}.xml");
        try
        {
            await Run(["--run", "-w", FakeWorkspaces.Root, "--report", path], new FakeRunService().FailOn(1));

            Assert.True(File.Exists(path));
            Assert.NotNull(XDocument.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_report_that_cannot_be_written_does_not_change_the_verdict()
    {
        // The run already happened. Turning a passing run into exit 2 because a path was not writable
        // would tell the build the wrong thing about the API.
        var unwritable = Path.Combine(Path.GetTempPath(), $"fubar-cli-{Guid.NewGuid():n}", " bad", "r.json");

        var exit = await Run(["--run", "-w", FakeWorkspaces.Root, "--report", unwritable]);

        Assert.Equal(Passed, exit);
        Assert.Contains("Could not write the report", Error);
    }

    /// <summary>
    /// A capture that found nothing does not fail its own step - the request answered, and whether a
    /// missing field matters is what an assertion is for. It has to be SAID on that step's line all
    /// the same: the failure it causes lands several steps later as a {{variable}} that never
    /// resolved, and without this the step that actually caused it reads "ok".
    /// </summary>
    [Fact]
    public async Task A_capture_that_found_nothing_is_said_on_the_step_that_could_not_capture_it()
    {
        var exit = await Run(
            ["--run", "-w", FakeWorkspaces.Root],
            new FakeRunService().CaptureFailureOn(1, "catId", "No value for body $.identifier."));

        // Still green: this step did what it was asked to.
        Assert.Equal(Passed, exit);

        Assert.Contains("could not capture {{catId}}", Output, StringComparison.Ordinal);
        Assert.Contains("$.identifier", Output, StringComparison.Ordinal);
    }

    // ---- Oracles reach the run -------------------------------------------------------------------
    //
    // These exist because the flags for them shipped once already, parsed correctly, and then did
    // nothing at all: CliRunner never looked at request.Oracle. Every test below asserts on what the
    // RUN was given, not on what the parser produced.

    [Fact]
    public async Task Without_an_oracle_nothing_judges_the_responses()
    {
        var runService = new FakeRunService();

        await Run(["--run", "-w", FakeWorkspaces.Root], runService);

        Assert.Equal(OracleKind.None, runService.LastRun!.Oracle!.Kind);
    }

    [Fact]
    public async Task The_snapshot_oracle_reaches_the_run()
    {
        var runService = new FakeRunService();

        await Run(["--run", "-w", FakeWorkspaces.Root, "--oracle", "snapshot"], runService);

        Assert.Equal(OracleKind.Snapshot, runService.LastRun!.Oracle!.Kind);

        // Nothing can be compared without them, and a run that asked for a comparison and kept no
        // bodies would report "no difference" for every step.
        Assert.True(runService.LastRun.Options.CaptureResponseBodies);
    }

    [Fact]
    public async Task The_environment_oracle_reaches_the_run_and_names_its_other_side()
    {
        var runService = new FakeRunService();

        await Run(
            ["--run", "-w", FakeWorkspaces.Root, "--env", "Staging", "--oracle", "env:Production"],
            runService,
            environments: new FakeEnvironments("Staging", "Production"));

        var oracle = Assert.IsType<EnvironmentOracle>(runService.LastRun!.Oracle);
        Assert.Equal("Production", oracle.OtherName);
    }

    /// <summary>Named and not found is an error here for the same reason it is for --env: comparing
    /// against an environment nobody has would compare against nothing.</summary>
    [Fact]
    public async Task An_environment_oracle_naming_an_unknown_environment_exits_could_not_run()
    {
        var exit = await Run(
            ["--run", "-w", FakeWorkspaces.Root, "--oracle", "env:Prod"],
            environments: new FakeEnvironments("Staging", "Production"));

        Assert.Equal(CouldNotRun, exit);
        Assert.Contains("Production", Error);
    }

    // ---- Recording -------------------------------------------------------------------------------

    [Fact]
    public async Task Update_snapshots_records_instead_of_running_a_comparison()
    {
        var runService = new FakeRunService();
        var recording = new FakeSnapshotRecording();

        var exit = await Run(
            ["--run", "-w", FakeWorkspaces.Root, "--env", "Staging", "--update-snapshots"],
            runService,
            environments: new FakeEnvironments("Staging"),
            recording: recording);

        Assert.Equal(Passed, exit);
        Assert.NotNull(recording.Last);
        Assert.Equal("Staging", recording.Last!.Environment!.Name);

        // Recording is not a run with a flag on it: the ordinary run path is not entered at all.
        Assert.Null(runService.LastRun);
    }

    /// <summary>Per environment by default: its failure mode is a redundant file, while a shared
    /// snapshot across environments holding different data reports a data difference as a
    /// regression.</summary>
    [Fact]
    public async Task Recording_is_per_environment_unless_shared_is_asked_for()
    {
        var perEnvironment = new FakeSnapshotRecording();
        await Run(
            ["--run", "-w", FakeWorkspaces.Root, "--env", "Staging", "--update-snapshots"],
            environments: new FakeEnvironments("Staging"),
            recording: perEnvironment);

        var shared = new FakeSnapshotRecording();
        await Run(
            ["--run", "-w", FakeWorkspaces.Root, "--env", "Staging", "--update-snapshots", "--shared-snapshots"],
            environments: new FakeEnvironments("Staging"),
            recording: shared);

        Assert.Equal(Fubar.Studio.Core.Snapshots.SnapshotScope.Environment, perEnvironment.Last!.Scope);
        Assert.Equal(Fubar.Studio.Core.Snapshots.SnapshotScope.Shared, shared.Last!.Scope);
    }

    // ---- Selectors -------------------------------------------------------------------------------

    [Fact]
    public async Task A_batch_selector_runs_the_batch_in_its_own_order()
    {
        var runService = new FakeRunService();
        var batches = new FakeBatches().With(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("auth/login"), new BatchStep("orders/get-order", "default")],
        });

        var exit = await Run(["run", "@smoke", "-w", FakeWorkspaces.Root], runService, batches: batches);

        Assert.Equal(Passed, exit);
        Assert.Equal(["auth/login", "orders/get-order"], runService.LastRun!.Plan.Steps.Select(s => s.Name));
    }

    /// <summary>A typo in a CI script must not pass by running nothing.</summary>
    [Fact]
    public async Task A_batch_that_does_not_exist_exits_could_not_run()
    {
        var exit = await Run(["run", "@nope", "-w", FakeWorkspaces.Root]);

        Assert.Equal(CouldNotRun, exit);
        Assert.Contains("nope", Error, StringComparison.Ordinal);
    }

    /// <summary>The batch says what it is for; the command line still wins, so a batch written for
    /// staging can be run against a branch deployment without editing the file.</summary>
    [Fact]
    public async Task A_batchs_oracle_and_environment_are_used_unless_the_command_line_says_otherwise()
    {
        var batches = new FakeBatches().With(new Batch
        {
            Name = "nightly",
            Steps = [new BatchStep("orders/get-order")],
            Oracle = new BatchOracle(BatchOracleKind.Snapshot),
            Environments = ["Staging"],
        });

        var fromBatch = new FakeRunService();
        await Run(["run", "@nightly", "-w", FakeWorkspaces.Root], fromBatch,
            environments: new FakeEnvironments("Staging", "Production"), batches: batches);

        Assert.Equal("Staging", fromBatch.LastRun!.Environment!.Name);
        Assert.Equal(OracleKind.Snapshot, fromBatch.LastRun.Oracle!.Kind);

        var overridden = new FakeRunService();
        await Run(["run", "@nightly", "-w", FakeWorkspaces.Root, "--env", "Production", "--oracle", "none"],
            overridden, environments: new FakeEnvironments("Staging", "Production"), batches: batches);

        Assert.Equal("Production", overridden.LastRun!.Environment!.Name);
        Assert.Equal(OracleKind.None, overridden.LastRun.Oracle!.Kind);
    }

    /// <summary>A batch cuts across the tree, so its rules are an overlay on whatever each endpoint
    /// already resolves to - and the run has to be given them, or they apply to nothing.</summary>
    [Fact]
    public async Task A_batchs_overlay_reaches_the_run()
    {
        var runService = new FakeRunService();
        var batches = new FakeBatches().With(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/get-order")],
            Overlay = new BatchOverlay
            {
                Tolerances = [new Fubar.Studio.Core.Comparison.Tolerance { Path = "$..elapsedMs", Numeric = 500 }],
            },
        });

        await Run(["run", "@smoke", "-w", FakeWorkspaces.Root], runService, batches: batches);

        Assert.NotNull(runService.LastRun!.Overlay);
    }

    [Fact]
    public async Task The_run_verb_with_no_selector_runs_the_whole_workspace()
    {
        var runService = new FakeRunService();

        var exit = await Run(["run", "-w", FakeWorkspaces.Root], runService);

        Assert.Equal(Passed, exit);
        Assert.Equal(2, runService.LastRun!.Plan.Count);
    }

    // ---- Fakes ---------------------------------------------------------------------------------

    private sealed class FakeRunService : ICollectionRunService
    {
        private readonly HashSet<int> _failures = [];
        private readonly HashSet<int> _errors = [];
        private (int Step, int Code)? _unexpected;

        public CollectionRun? LastRun { get; private set; }

        public FakeRunService FailOn(int step) { _failures.Add(step); return this; }

        public FakeRunService ErrorOn(int step) { _errors.Add(step); return this; }

        public FakeRunService UnexpectedStatusOn(int step, int code) { _unexpected = (step, code); return this; }

        private (int Step, string Variable, string Error)? _captureFailure;

        public FakeRunService CaptureFailureOn(int step, string variable, string error)
        {
            _captureFailure = (step, variable, error);
            return this;
        }

        public Task<RunReport> RunAsync(
            CollectionRun run,
            IProgress<RunProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRun = run;

            var steps = new List<StepReport>();
            foreach (var step in run.Plan.Steps)
            {
                var report = Build(step);
                steps.Add(report);
                progress?.Report(RunProgress.Finished(report, run.Plan.Count));
            }

            return Task.FromResult(new RunReport(steps, 100, false, false));
        }

        private StepReport Build(RunStep step)
        {
            var n = step.Order;

            if (_errors.Contains(n))
            {
                return new StepReport(step, StepStatus.Errored, null, null, 0, 0, [], [], "No such host is known.");
            }

            if (_unexpected is { } u && u.Step == n)
            {
                return new StepReport(step, StepStatus.Passed, u.Code, "Unavailable", 5, 0, [], [], null);
            }

            IReadOnlyList<AssertionResult> assertions = _failures.Contains(n)
                ? [new AssertionResult(false, "status is 200", "500")]
                : [new AssertionResult(true, "status is 200", "200")];

            IReadOnlyList<CaptureResult> captures = _captureFailure is { } c && c.Step == n
                ? [new CaptureResult(false, c.Variable, null, "Session", c.Error)]
                : [];

            return new StepReport(
                step,
                _failures.Contains(n) ? StepStatus.Failed : StepStatus.Passed,
                200, "OK", 12, 100, assertions, captures, null);
        }
    }

    private sealed class FakeWorkspaces : IWorkspaceStore
    {
        public const string Root = "/ws";

        public bool IsWorkspaceRoot(string directoryPath) =>
            Path.GetFullPath(directoryPath)
                .EndsWith(Path.GetFullPath(Root).TrimStart(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        public Task<Workspace> LoadWorkspaceAsync(string rootPath, CancellationToken ct = default) =>
            Task.FromResult(new Workspace { RootPath = rootPath, Manifest = new AppManifest { Name = "t" } });

        public Task<Workspace> CreateWorkspaceAsync(string rootPath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveAppManifestAsync(string rootPath, AppManifest manifest, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///   collections/
    ///     Orders/
    ///       Create.json
    ///       Get.json
    /// </summary>
    private sealed class FakeRequests : IRequestStore
    {
        /// <summary>No migration happens in a fake store, so nothing ever raises this.</summary>
        public event Action<string, IReadOnlyList<string>>? RequestMigrated { add { } remove { } }
        public IReadOnlyList<WorkspaceTreeNode> BuildCollectionsTree(string rootPath)
        {
            var orders = Path.GetFullPath(Path.Combine(rootPath, "collections", "Orders"));
            return
            [
                new WorkspaceTreeNode("Orders", orders, true,
                [
                    new WorkspaceTreeNode("Create.json", Path.Combine(orders, "Create.json"), false, [], new RequestSummary("POST", false)),
                    new WorkspaceTreeNode("Get.json", Path.Combine(orders, "Get.json"), false, [], new RequestSummary("GET", false)),
                ]),
            ];
        }

        public Task<RequestModel> LoadRequestAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(new RequestModel { Name = Path.GetFileNameWithoutExtension(path) });

        public Task SaveRequestAsync(string path, RequestModel request, CancellationToken ct = default) => throw new NotSupportedException();

        public string CreateRequest(string parentDirectory, string requestName) => throw new NotSupportedException();

        public string CreateFolder(string parentDirectory, string folderName) => throw new NotSupportedException();

        public string DuplicatePath(string path) => throw new NotSupportedException();

        public string RenamePath(string path, string newName) => throw new NotSupportedException();

        public void DeletePath(string path) => throw new NotSupportedException();
    }

    /// <summary>Nothing recorded anywhere, which is what makes "no snapshot is never a pass" testable
    /// from the command line.</summary>
    private sealed class FakeSnapshots : Fubar.Studio.Core.Snapshots.ISnapshotStore
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

    private sealed class FakeSnapshotRecording : ISnapshotRecordingService
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

    private sealed class FakeBatches : IBatchPlanner
    {
        private readonly Dictionary<string, Batch> _batches = new(StringComparer.OrdinalIgnoreCase);

        public FakeBatches With(Batch batch)
        {
            _batches[batch.Name] = batch;
            return this;
        }

        /// <summary>Keyed by the qualified name, so a test can pin that the CLI passed the OWNER as
        /// well as the name - <c>@happy</c> and <c>orders/get-order@happy</c> are different batches.</summary>
        public Task<ResolvedBatch> ExpandAsync(
            Workspace workspace,
            string batchName,
            string? ownerPath = null,
            CancellationToken cancellationToken = default)
        {
            var key = ownerPath is { Length: > 0 } ? $"{ownerPath}@{batchName}" : batchName;

            if (!_batches.TryGetValue(key, out var batch))
            {
                throw new InvalidOperationException($"There is no batch called \"{key}\".");
            }

            var steps = batch.Steps
                .Select((s, i) => new RunStep(i + 1, s.Endpoint, $"/ws/collections/{s.Endpoint}", "/ws/collections", s.Case))
                .ToList();

            return Task.FromResult(new ResolvedBatch(batch, new RunPlan(steps)));
        }
    }

    private sealed class FakeEnvironments(params string[] names) : IEnvironmentStore
    {
        public Task<IReadOnlyList<WorkspaceEnvironment>> LoadEnvironmentsAsync(string rootPath, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceEnvironment>>(
                [.. names.Select(n => new WorkspaceEnvironment { Id = n.ToLowerInvariant(), Name = n })]);

        public Task SaveEnvironmentAsync(string rootPath, WorkspaceEnvironment environment, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteEnvironmentAsync(string rootPath, string environmentId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
