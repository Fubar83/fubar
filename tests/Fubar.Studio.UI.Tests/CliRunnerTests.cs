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
        FakeEnvironments? environments = null) =>
        CliRunner.RunAsync(
            CommandLine.Parse(args),
            runService ?? new FakeRunService(),
            workspaces ?? new FakeWorkspaces(),
            requests ?? new FakeRequests(),
            environments ?? new FakeEnvironments(),
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
        Assert.Contains("not a folder or request", Error);
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

            return new StepReport(
                step,
                _failures.Contains(n) ? StepStatus.Failed : StepStatus.Passed,
                200, "OK", 12, 100, assertions, [], null);
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
