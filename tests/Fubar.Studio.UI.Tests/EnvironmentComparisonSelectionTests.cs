using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Infrastructure;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Picking a row in the comparison window and getting its diff.
///
/// <para>Reported as "I cannot see the diffs nor click in the left pane". The click handler was wired
/// the whole time; what was not was anything downstream saying so when it failed.</para>
/// </summary>
public class EnvironmentComparisonSelectionTests
{
    private static readonly Workspace Ws = new() { RootPath = "/w", Manifest = new AppManifest { Name = "t" } };

    private static readonly WorkspaceEnvironment Staging = new() { Id = "stg", Name = "Staging" };
    private static readonly WorkspaceEnvironment Prod = new() { Id = "prd", Name = "Production" };

    /// <summary>Two cases of ONE endpoint - the shape this window exists for, and the one that used to
    /// throw before a request was sent.</summary>
    private static RunPlan TwoCasesOfOneEndpoint() => new(
    [
        new RunStep(1, "get-order", "/w/collections/get-order/endpoint.json", "/w/collections",
            "found", "/w/collections/get-order/cases/found.json"),
        new RunStep(2, "get-order", "/w/collections/get-order/endpoint.json", "/w/collections",
            "missing", "/w/collections/get-order/cases/missing.json"),
    ]);

    private static IFileComparisonService Comparison()
    {
        var services = new ServiceCollection();
        services.AddFubarDiffTextAndJson();
        services.AddSingleton<JsonSemanticPass>();
        services.AddSingleton<IFileComparisonService, FileComparisonService>();
        return services.BuildServiceProvider().GetRequiredService<IFileComparisonService>();
    }

    /// <summary>Answers per environment, so the two sides genuinely differ.</summary>
    private sealed class FakePairRun : IEnvironmentPairRunService
    {
        public Task<EnvironmentPairReport> RunAsync(
            EnvironmentPairRun run,
            IProgress<StepPairProgress>? progress = null,
            CancellationToken ct = default)
        {
            var pairs = new List<StepPair>();

            foreach (var step in run.Plan.Steps)
            {
                progress?.Report(StepPairProgress.Starting(step, run.Plan.Count));

                var pair = new StepPair(step, Answer(step, "stg"), Answer(step, "prd"));
                pairs.Add(pair);
                progress?.Report(StepPairProgress.Complete(pair, run.Plan.Count));
            }

            return Task.FromResult(new EnvironmentPairReport("Staging", "Production", pairs, 1, false, false));
        }

        private static StepReport Answer(RunStep step, string environment) =>
            new(step, StepStatus.Passed, 200, "OK", 1, 2, [], [], null)
            {
                ResponseBody = $$"""{"case":"{{step.CaseName}}","env":"{{environment}}"}""",
                ContentType = "application/json",
            };
    }

    private sealed class FakeComparer : IResponseComparer
    {
        public Task<ComparisonOutcome> CompareAsync(
            string left, string right, ResolvedComparisonSettings settings, CancellationToken ct = default) =>
            Task.FromResult(string.Equals(left, right, StringComparison.Ordinal)
                ? ComparisonOutcome.Identical
                : new ComparisonOutcome(1, true, []));
    }

    /// <summary>Resolves rules, or refuses to - which is the failure the window used to swallow.</summary>
    private sealed class FakeSettingsContext(Exception? fails = null) : IComparisonSettingsContext
    {
        public Task<DiffSettingsContext> BuildAsync(
            Workspace workspace, string requestPath, Func<Task>? onSaved = null) =>
            fails is null
                ? Task.FromResult(new DiffSettingsContext([], null, "orders", (_, _) => Task.CompletedTask))
                : Task.FromException<DiffSettingsContext>(fails);
    }

    private static EnvironmentComparisonViewModel Vm(
        RunPlan? plan = null, IComparisonSettingsContext? settings = null) =>
        new(new FakePairRun(), Comparison(), new FakeComparer(), settings ?? new FakeSettingsContext(),
            plan ?? TwoCasesOfOneEndpoint(), Ws, [Staging, Prod], "get-order");

    // ---- Two cases of one endpoint ---------------------------------------------------------------

    /// <summary>
    /// The rows were keyed by FILE PATH, and every case of an endpoint lives in the same
    /// <c>endpoint.json</c> - so building that dictionary threw "an item with the same key has already
    /// been added" before a single request was sent, and the run reported "Could not run".
    /// </summary>
    [Fact]
    public async Task Two_cases_of_one_endpoint_each_get_their_own_row()
    {
        var vm = Vm();

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.NotNull(r.Pair));

        // And each row got its OWN answer rather than both landing on whichever won the key.
        Assert.Contains("found", vm.Rows[0].Pair!.Left.ResponseBody);
        Assert.Contains("missing", vm.Rows[1].Pair!.Left.ResponseBody);
    }

    // ---- Selecting a row -------------------------------------------------------------------------

    [Fact]
    public async Task Selecting_a_row_puts_its_two_answers_in_the_pane()
    {
        var vm = Vm();
        await vm.RunCommand.ExecuteAsync(null);

        await vm.SelectAsync(vm.Rows[1]);

        Assert.True(vm.Rows[1].IsSelected);
        Assert.NotNull(vm.Diff.Pane.LeftDocument);
        Assert.NotNull(vm.Diff.Pane.RightDocument);
        Assert.Contains("missing", vm.Diff.Pane.LeftDocument!.Text);
    }

    [Fact]
    public async Task Selecting_another_row_deselects_the_first()
    {
        var vm = Vm();
        await vm.RunCommand.ExecuteAsync(null);

        await vm.SelectAsync(vm.Rows[0]);
        await vm.SelectAsync(vm.Rows[1]);

        Assert.False(vm.Rows[0].IsSelected);
        Assert.True(vm.Rows[1].IsSelected);
    }

    /// <summary>
    /// The reported symptom. Resolving the rules reads files, and the only caller of the load is a
    /// property-changed handler that discards the task - so an exception vanished entirely: the row
    /// highlighted, the pane stayed on "Nothing selected yet", and clicking looked like it did nothing.
    /// </summary>
    [Fact]
    public async Task A_row_that_cannot_be_opened_says_so_instead_of_doing_nothing()
    {
        var vm = Vm(settings: new FakeSettingsContext(new IOException("the folder config is unreadable")));
        await vm.RunCommand.ExecuteAsync(null);

        await vm.SelectAsync(vm.Rows[0]);

        Assert.Contains("unreadable", vm.SelectionError ?? "", StringComparison.Ordinal);
        Assert.Contains("unreadable", vm.Rows[0].Note ?? "", StringComparison.Ordinal);
    }

    /// <summary>The point of interleaving: the first answered row is on screen before the rest have
    /// been sent, so rules can be written while the run is still going.</summary>
    [Fact]
    public async Task The_first_row_to_answer_is_selected_on_its_own()
    {
        var vm = Vm();

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Same(vm.Rows[0], vm.SelectedRow);

        // And its diff is actually on screen - a selected row beside an empty pane is the same
        // failure wearing a highlight.
        await vm.SelectAsync(vm.SelectedRow);
        Assert.NotNull(vm.Diff.Pane.LeftDocument);
    }
}
