using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// Cleanup steps.
///
/// <para>The problem they exist for: a chain that creates something needs to remove it again, and
/// <c>stopOnFailure</c> - the right setting for a chain - guarantees a failure in the middle skips the
/// delete. Every red run then leaves a row behind, which over a week of failing CI is a lot of rows
/// nobody deletes.</para>
/// </summary>
public class TeardownTests
{
    private static readonly Workspace Ws = new() { RootPath = "/w", Manifest = new AppManifest { Name = "t" } };

    private static RunStep Step(int n, bool teardown = false) =>
        new(n, $"r{n}", $"/w/collections/r{n}/request.json", "/w/collections") { IsTeardown = teardown };

    /// <summary>Three tested steps and one cleanup, which is what a create/get/update/delete chain
    /// with a best-effort delete looks like.</summary>
    private static RunPlan Plan() =>
        new([Step(1), Step(2), Step(3), Step(4, teardown: true)]);

    private static CollectionRunService Sut(FakeExecution execution) =>
        new(execution, new FakeStore(), new FakeInheritance(), new FakeProfiles(),
            new FakeComparer(), new FakeComparisonSettings(), new FakeEndpoints());

    private static CollectionRun Run(RunOptions? options = null) =>
        new(Plan(), Ws, null, options ?? RunOptions.Default);

    // ---- When it runs ----------------------------------------------------------------------------

    [Fact]
    public async Task Cleanup_runs_last()
    {
        var execution = new FakeExecution();

        await Sut(execution).RunAsync(Run());

        Assert.Equal(["r1@none", "r2@none", "r3@none", "r4@none"], execution.Sent);
    }

    /// <summary>The whole point. Stopping at the first failure is right for a chain and is exactly
    /// what skips the delete, so cleanup has to be held back and run anyway.</summary>
    [Fact]
    public async Task Cleanup_runs_even_when_the_run_stopped_at_the_first_failure()
    {
        var execution = new FakeExecution().ErrorOn("none");

        var report = await Sut(execution)
            .RunAsync(Run(RunOptions.Default with { StopOnFailure = true }));

        // Stopped after r1 errored - and r4 still went out.
        Assert.Equal(["r1@none", "r4@none"], execution.Sent);
        Assert.Equal(StepStatus.Errored, report.Cleanup.Single().Status);
    }

    /// <summary>Cancelling means stop. Sending four more requests after Ctrl-C is the opposite of
    /// stopping - it leaks, and that is the lesser surprise of the two.</summary>
    [Fact]
    public async Task Cleanup_does_not_run_after_a_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var execution = new FakeExecution().CancelOn("r2@none", cancellation);

        var report = await Sut(execution).RunAsync(Run(), null, cancellation.Token);

        Assert.DoesNotContain("r4@none", execution.Sent);
        Assert.Equal(StepStatus.Skipped, report.Cleanup.Single().Status);
        Assert.True(report.WasCancelled);
    }

    // ---- What it does to the verdict -------------------------------------------------------------

    /// <summary>A cleanup failure is almost always a consequence of the failure above it - deleting
    /// what was never created - and one that cried wolf on every already-red run would train people to
    /// ignore the one that matters.</summary>
    [Fact]
    public async Task A_failed_cleanup_does_not_fail_the_run()
    {
        var execution = new FakeExecution().ErrorOnStep("r4");

        var report = await Sut(execution).RunAsync(Run());

        Assert.True(report.Ok);
        Assert.Equal(0, report.Errored);
        Assert.Equal(1, report.CleanupFailed);
    }

    /// <summary>Never part of the verdict, and never silent either: a leak nobody hears about is what
    /// teardown exists to prevent.</summary>
    [Fact]
    public async Task A_failed_cleanup_is_said_in_the_summary()
    {
        var execution = new FakeExecution().ErrorOnStep("r4");

        var report = await Sut(execution).RunAsync(Run());

        Assert.Contains("cleanup", report.Summary(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"3/3 passed", not "4/4": the fourth was housekeeping, and counting it would inflate
    /// every count in the report by the number of things the test tidied up after itself.</summary>
    [Fact]
    public async Task Cleanup_is_not_counted_among_the_steps_that_were_tested()
    {
        var report = await Sut(new FakeExecution()).RunAsync(Run());

        Assert.Equal(3, report.Total);
        Assert.Equal(3, report.Passed);

        // Still IN the report, so it appears on the console and in the JUnit output like anything else.
        Assert.Equal(4, report.Steps.Count);
        Assert.Single(report.Cleanup);
    }

    /// <summary>
    /// A batch reusing "delete-cat#created" as cleanup reuses a case that expects 204, and on a run
    /// where the delete already happened as a STEP the cleanup finds a 404. That is the expected
    /// happy path, and reporting it as a failed cleanup on every successful run is exactly the crying
    /// wolf teardown exists to avoid. What still counts is whether it could be SENT.
    /// </summary>
    [Fact]
    public async Task Cleanup_does_not_run_the_cases_assertions()
    {
        // The fake fails every assertion it is given for r4.
        var report = await Sut(new FakeExecution().AssertionFailureOn("r4")).RunAsync(Run());

        var cleanup = report.Cleanup.Single();
        Assert.Equal(StepStatus.Passed, cleanup.Status);
        Assert.Empty(cleanup.Assertions);
        Assert.Equal(0, report.CleanupFailed);
    }

    /// <summary>What DOES count: cleanup that could not be sent at all. That is a leak.</summary>
    [Fact]
    public async Task Cleanup_that_could_not_be_sent_still_counts()
    {
        var report = await Sut(new FakeExecution().ErrorOnStep("r4")).RunAsync(Run());

        Assert.Equal(StepStatus.Errored, report.Cleanup.Single().Status);
        Assert.Equal(1, report.CleanupFailed);
    }

    /// <summary>Cleanup is not being tested, so there is nothing to compare it against - and a missing
    /// snapshot for a delete nobody recorded would fail a run that was otherwise fine.</summary>
    [Fact]
    public async Task Cleanup_is_never_judged_by_the_oracle()
    {
        var comparer = new FakeComparer();

        var report = await new CollectionRunService(
                new FakeExecution().Body("""{"a":1}"""), new FakeStore(), new FakeInheritance(),
                new FakeProfiles(), comparer, new FakeComparisonSettings(), new FakeEndpoints())
            .RunAsync(new CollectionRun(
                Plan(),
                Ws,
                null,
                RunOptions.Default with { CaptureResponseBodies = true },
                new AlwaysMissingOracle()));

        Assert.Equal(ComparisonVerdict.NotCompared, report.Cleanup.Single().Comparison);

        // The tested steps still are - this is not "the oracle stopped working".
        Assert.All(report.Judged, s => Assert.Equal(ComparisonVerdict.Unavailable, s.Comparison));
    }

    private sealed class AlwaysMissingOracle : IOracle
    {
        public OracleKind Kind => OracleKind.Snapshot;

        public Task<OtherSide> ObtainAsync(OracleContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(OtherSide.Missing("No snapshot recorded"));
    }
}
