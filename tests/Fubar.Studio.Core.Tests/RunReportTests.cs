using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// What a run's verdict counts, and - more importantly - what it deliberately does not.
/// </summary>
public class RunReportTests
{
    private static RunStep Step(int n) => new(n, $"r{n}", $"/w/r{n}/request.json", "/w");

    private static StepReport Report(
        int n,
        StepStatus status,
        int? statusCode = 200,
        IReadOnlyList<AssertionResult>? assertions = null,
        IReadOnlyList<CaptureResult>? captures = null,
        string? error = null) =>
        new(Step(n), status, statusCode, "OK", 10, 100, assertions ?? [], captures ?? [], error);

    private static AssertionResult Pass() => new(true, "status is 200", "200");

    private static AssertionResult Fail() => new(false, "status is 200", "500");

    private static RunReport Of(params StepReport[] steps) => new(steps, 100, false, false);

    // ---- The decision the whole feature rests on -----------------------------------------------

    [Fact]
    public void A_500_with_no_assertion_does_NOT_fail_the_run()
    {
        // Load-bearing and not obvious. This app lets you assert `StatusCode Equals 404` deliberately,
        // so a runner that ALSO treated 4xx/5xx as failure would make the same response both the
        // expected result and a failure. Deciding which statuses are bad is what assertions are for.
        var report = Of(Report(1, StepStatus.Passed, statusCode: 500));

        Assert.True(report.Ok);
        Assert.Equal(0, report.Failed);
    }

    [Fact]
    public void But_it_is_REPORTED_so_nothing_hides()
    {
        // The other half of that bargain: the run does not fail, and the reader is still told.
        var report = Of(
            Report(1, StepStatus.Passed, statusCode: 500),
            Report(2, StepStatus.Passed, statusCode: 200));

        var flagged = Assert.Single(report.UnexpectedStatuses);
        Assert.Equal("r1", flagged.Step.Name);
    }

    [Fact]
    public void A_non_2xx_that_an_assertion_already_judged_is_not_flagged_again()
    {
        // Asserting `StatusCode Equals 404` and then being told the 404 was unexpected would be the
        // report arguing with the user about the thing they just told it to expect.
        var report = Of(Report(1, StepStatus.Passed, statusCode: 404, assertions: [Pass()]));

        Assert.Empty(report.UnexpectedStatuses);
    }

    [Fact]
    public void A_failed_assertion_fails_the_run()
    {
        var report = Of(Report(1, StepStatus.Failed, assertions: [Pass(), Fail()]));

        Assert.False(report.Ok);
        Assert.Equal(1, report.Failed);
        Assert.Equal(1, report.AssertionsFailed);
        Assert.Equal(1, report.AssertionsPassed);
    }

    [Fact]
    public void A_transport_error_fails_the_run()
    {
        var report = Of(Report(1, StepStatus.Errored, statusCode: null, error: "No such host"));

        Assert.False(report.Ok);
        Assert.Equal(1, report.Errored);
    }

    // ---- Cancellation --------------------------------------------------------------------------

    [Fact]
    public void A_cancelled_run_is_never_green_even_when_everything_it_ran_passed()
    {
        // It did not answer the question that was asked. Reporting green for a run stopped half way is
        // how a runner stops being believed.
        var report = new RunReport([Report(1, StepStatus.Passed)], 100, WasCancelled: true, StoppedEarly: false);

        Assert.False(report.Ok);
    }

    [Fact]
    public void Skipped_steps_keep_the_run_from_passing()
    {
        var report = Of(Report(1, StepStatus.Passed), StepReport.SkippedStep(Step(2)));

        Assert.False(report.Ok);
        Assert.Equal(1, report.Skipped);
    }

    [Fact]
    public void An_empty_run_is_not_a_pass()
    {
        Assert.False(RunReport.Empty.Ok);
        Assert.Equal("Nothing to run.", RunReport.Empty.Summary());
    }

    [Fact]
    public void Everything_green_passes()
    {
        Assert.True(Of(Report(1, StepStatus.Passed, assertions: [Pass()]), Report(2, StepStatus.Passed)).Ok);
    }

    // ---- The summary line ----------------------------------------------------------------------

    [Fact]
    public void The_summary_leads_with_the_count_that_matters()
    {
        var report = Of(Report(1, StepStatus.Passed), Report(2, StepStatus.Failed, assertions: [Fail()]));

        Assert.StartsWith("1/2 passed", report.Summary());
    }

    [Fact]
    public void The_summary_names_every_non_zero_bucket_and_no_others()
    {
        var line = Of(
            Report(1, StepStatus.Passed),
            Report(2, StepStatus.Failed, assertions: [Fail()]),
            Report(3, StepStatus.Errored, statusCode: null, error: "boom")).Summary();

        Assert.Contains("1 failed", line);
        Assert.Contains("1 errored", line);
        Assert.Contains("1 assertion failed", line);   // singular
        Assert.DoesNotContain("skipped", line);
    }

    [Fact]
    public void The_summary_says_when_it_was_cancelled()
    {
        var report = new RunReport([Report(1, StepStatus.Passed)], 100, WasCancelled: true, StoppedEarly: false);

        Assert.EndsWith("(cancelled)", report.Summary());
    }

    [Fact]
    public void Assertion_counts_add_up_across_steps()
    {
        var report = Of(
            Report(1, StepStatus.Passed, assertions: [Pass(), Pass()]),
            Report(2, StepStatus.Failed, assertions: [Pass(), Fail(), Fail()]));

        Assert.Equal(3, report.AssertionsPassed);
        Assert.Equal(2, report.AssertionsFailed);
    }
}
