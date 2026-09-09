using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Application.Requests;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Snapshots;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// What a run does with an oracle's answer.
///
/// <para>The rule everything else hangs off: a missing other side is never a pass. "Nothing to compare,
/// therefore fine" is how a suite silently stops testing, and it is the failure mode a regression
/// feature has to be built to refuse.</para>
/// </summary>
public class OracleTests
{
    private static readonly Workspace Ws = new() { RootPath = "/w", Manifest = new AppManifest { Name = "t" } };

    private static readonly WorkspaceEnvironment Staging = new() { Id = "stg", Name = "Staging" };

    private static RunStep Step(int n) => new(n, $"r{n}", $"/w/collections/r{n}/request.json", "/w/collections");

    private static RunPlan Plan(int count) => new([.. Enumerable.Range(1, count).Select(Step)]);

    private static CollectionRunService Sut(
        FakeExecution execution, FakeComparer? comparer = null, FakeComparisonSettings? settings = null) =>
        new(execution, new FakeStore(), new FakeInheritance(), new FakeProfiles(),
            comparer ?? new FakeComparer(), settings ?? new FakeComparisonSettings());

    private static CollectionRun Run(IOracle? oracle, int steps = 1) =>
        new(Plan(steps), Ws, Staging, RunOptions.Default with { CaptureResponseBodies = true }, oracle);

    // ---- No oracle -------------------------------------------------------------------------------

    [Fact]
    public async Task Without_an_oracle_nothing_is_compared()
    {
        var comparer = new FakeComparer();

        var report = await Sut(new FakeExecution().Body("""{"a":1}"""), comparer)
            .RunAsync(Run(NoOracle.Instance));

        Assert.Empty(comparer.Compared);
        Assert.Equal(ComparisonVerdict.NotCompared, report.Steps[0].Comparison);
        Assert.True(report.Ok);
    }

    // ---- The snapshot oracle ---------------------------------------------------------------------

    [Fact]
    public async Task A_matching_snapshot_is_the_same_and_names_the_file_it_used()
    {
        var store = new FakeSnapshots().With("staging", """{"a":1}""");

        var report = await Sut(new FakeExecution().Body("""{"a":1}"""))
            .RunAsync(Run(new SnapshotOracle(store)));

        Assert.Equal(ComparisonVerdict.Same, report.Steps[0].Comparison);
        Assert.Equal("snapshots/staging.json", report.Steps[0].ComparedAgainst);
        Assert.True(report.Ok);
    }

    [Fact]
    public async Task A_differing_snapshot_fails_the_run_without_failing_the_request()
    {
        var store = new FakeSnapshots().With("staging", """{"a":1}""");

        var report = await Sut(new FakeExecution().Body("""{"a":2}"""))
            .RunAsync(Run(new SnapshotOracle(store)));

        // The request itself was fine - it sent, it answered, its assertions passed.
        Assert.Equal(StepStatus.Passed, report.Steps[0].Status);

        // What differs is the ANSWER, which is a separate axis and still fails the run.
        Assert.Equal(ComparisonVerdict.Differs, report.Steps[0].Comparison);
        Assert.Equal(1, report.Steps[0].DifferenceCount);
        Assert.False(report.Ok);
    }

    /// <summary>The rule this whole feature is built to refuse.</summary>
    [Fact]
    public async Task No_snapshot_is_reported_and_is_never_a_pass()
    {
        var report = await Sut(new FakeExecution().Body("""{"a":1}"""))
            .RunAsync(Run(new SnapshotOracle(new FakeSnapshots())));

        Assert.Equal(ComparisonVerdict.Unavailable, report.Steps[0].Comparison);
        Assert.Contains("Staging", report.Steps[0].ComparisonUnavailableReason!, StringComparison.Ordinal);
        Assert.False(report.Ok);
        Assert.Equal(1, report.Uncomparable);
    }

    /// <summary>A step that never answered has nothing to compare. Reporting "no snapshot" for it would
    /// blame the wrong thing - the failure is that the request did not run.</summary>
    [Fact]
    public async Task A_request_that_never_answered_is_not_blamed_on_a_missing_snapshot()
    {
        var report = await Sut(new FakeExecution().ErrorOn("stg"))
            .RunAsync(Run(new SnapshotOracle(new FakeSnapshots())));

        Assert.Equal(StepStatus.Errored, report.Steps[0].Status);
        Assert.Equal(ComparisonVerdict.NotCompared, report.Steps[0].Comparison);
    }

    /// <summary>An oracle that throws must not take the run down with it: the other nineteen requests
    /// have answers worth having.</summary>
    [Fact]
    public async Task An_oracle_that_throws_makes_one_step_uncomparable_not_a_failed_run()
    {
        var report = await Sut(new FakeExecution().Body("""{"a":1}"""))
            .RunAsync(Run(new ThrowingOracle(), steps: 2));

        Assert.Equal(2, report.Total);
        Assert.All(report.Steps, s => Assert.Equal(ComparisonVerdict.Unavailable, s.Comparison));
        Assert.Contains("nope", report.Steps[0].ComparisonUnavailableReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_snapshot_is_the_left_side_so_a_diff_reads_old_to_new()
    {
        var comparer = new FakeComparer();
        var store = new FakeSnapshots().With("staging", """{"was":true}""");

        await Sut(new FakeExecution().Body("""{"is":true}"""), comparer)
            .RunAsync(Run(new SnapshotOracle(store)));

        var (left, right) = Assert.Single(comparer.Compared);
        Assert.Equal("""{"was":true}""", left);
        Assert.Equal("""{"is":true}""", right);
    }

    // ---- Tolerances ------------------------------------------------------------------------------

    /// <summary>The tolerance is resolved from the chain and applied to what the comparer found - the
    /// wiring, not the arithmetic, which is <c>ToleranceTests</c>' business.</summary>
    [Fact]
    public async Task A_difference_within_tolerance_does_not_fail_the_run_and_is_still_counted()
    {
        var store = new FakeSnapshots().With("staging", """{"total":10.00}""");

        var report = await Sut(
                new FakeExecution().Body("""{"total":10.005}"""),
                Differing("$.total", "10.00", "10.005"),
                new FakeComparisonSettings().Tolerating(new Tolerance { Path = "$.total", Numeric = 0.01 }))
            .RunAsync(Run(new SnapshotOracle(store)));

        Assert.Equal(ComparisonVerdict.Same, report.Steps[0].Comparison);
        Assert.Equal(0, report.Steps[0].DifferenceCount);

        // Not folded into the green: "matched" and "was within tolerance" are different facts, and the
        // second is what you want to see when a tolerance turns out to be too generous.
        Assert.Equal(1, report.Steps[0].ToleratedCount);
        Assert.True(report.Ok);
    }

    [Fact]
    public async Task A_difference_outside_tolerance_still_fails_the_run()
    {
        var store = new FakeSnapshots().With("staging", """{"total":10.00}""");

        var report = await Sut(
                new FakeExecution().Body("""{"total":99.00}"""),
                Differing("$.total", "10.00", "99.00"),
                new FakeComparisonSettings().Tolerating(new Tolerance { Path = "$.total", Numeric = 0.01 }))
            .RunAsync(Run(new SnapshotOracle(store)));

        Assert.Equal(ComparisonVerdict.Differs, report.Steps[0].Comparison);
        Assert.False(report.Ok);
    }

    private static FakeComparer Differing(string path, string left, string right) =>
        new((_, _) => new ComparisonOutcome(
            1, true, [new ResponseDifference(path, left, right, ResponseDifferenceKind.Changed)]));

    // ---- Fakes ------------------------------------------------------------------------------------

    private sealed class FakeSnapshots : ISnapshotStore
    {
        private readonly Dictionary<string, string> _byScope = new(StringComparer.OrdinalIgnoreCase);

        public FakeSnapshots With(string scope, string body)
        {
            _byScope[scope] = body;
            return this;
        }

        public Task<SnapshotLookup> FindAsync(
            string workspaceRoot, string requestPath, string? environmentName, CancellationToken ct = default)
        {
            foreach (var scope in new[] { environmentName?.ToLowerInvariant(), "_shared" })
            {
                if (scope is not null && _byScope.TryGetValue(scope, out var body))
                {
                    return Task.FromResult(new SnapshotLookup(
                        new ResponseSnapshot { BodyFormat = "text", BodyText = body },
                        $"snapshots/{scope}.json"));
                }
            }

            return Task.FromResult(SnapshotLookup.None);
        }

        public Task SaveAsync(string workspaceRoot, string requestPath, ResponseSnapshot snapshot, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ScopesAsync(string workspaceRoot, string requestPath, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([.. _byScope.Keys]);
    }

    private sealed class ThrowingOracle : IOracle
    {
        public OracleKind Kind => OracleKind.Snapshot;

        public Task<OtherSide> ObtainAsync(OracleContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("nope");
    }
}
