using Fubar.Studio.Core.Snapshots;
using System.Diagnostics;
using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Running;

/// <inheritdoc cref="ICollectionRunService"/>
public sealed class CollectionRunService : ICollectionRunService, IEnvironmentPairRunService
{
    private readonly IRequestExecutionService _execution;
    private readonly IRequestStore _requests;
    private readonly IInheritanceResolver _inheritance;
    private readonly IAuthProfileStore _authProfiles;
    private readonly IResponseComparer _comparer;
    private readonly IRequestComparisonSettings _settings;
    private readonly IEndpointStore _endpoints;

    public CollectionRunService(
        IRequestExecutionService execution,
        IRequestStore requests,
        IInheritanceResolver inheritance,
        IAuthProfileStore authProfiles,
        IResponseComparer comparer,
        IRequestComparisonSettings settings,
        IEndpointStore endpoints)
    {
        _execution = execution;
        _requests = requests;
        _inheritance = inheritance;
        _authProfiles = authProfiles;
        _comparer = comparer;
        _settings = settings;
        _endpoints = endpoints;
    }

    public async Task<RunReport> RunAsync(
        CollectionRun run,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Plan.IsEmpty)
        {
            return RunReport.Empty;
        }

        // Auth profiles are read ONCE for the whole run, not per request. They are workspace-level and
        // do not change mid-run; re-reading them 200 times would be 200 file reads to get the same
        // answer, and would also let a run half way through pick up an edit and behave differently
        // before and after it.
        var profiles = await _authProfiles.LoadAuthProfilesAsync(run.Workspace.RootPath, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var reports = new List<StepReport>(run.Plan.Count);
        var total = run.Plan.Count;
        var cancelled = false;
        var stoppedEarly = false;

        // Cleanup is held back and run at the end, whatever happens above - see the teardown block
        // below and RunStep.IsTeardown.
        var tested = run.Plan.Steps.Where(s => !s.IsTeardown).ToList();
        var teardown = run.Plan.Steps.Where(s => s.IsTeardown).ToList();

        foreach (var step in tested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            // The delay goes BEFORE every request but the first, so a run of one request is never
            // slowed by a setting meant to space out a run of fifty.
            if (run.Options.DelayMilliseconds > 0 && reports.Count > 0)
            {
                try
                {
                    await Task.Delay(run.Options.DelayMilliseconds, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
            }

            progress?.Report(RunProgress.Starting(step, total));

            StepReport report;
            try
            {
                report = await RunStepAsync(step, run, profiles, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Cancelled mid-send. The request is reported as SKIPPED rather than errored: nobody
                // gave it a chance to answer, and a red row against it would read as the request being
                // broken. Cancelling must never manufacture failures - see RunReport.Ok, which refuses
                // to call a cancelled run green for the mirror-image reason.
                reports.Add(StepReport.SkippedStep(step));
                cancelled = true;
                break;
            }

            report = await JudgeAsync(report, run, cancellationToken).ConfigureAwait(false);

            reports.Add(report);
            progress?.Report(RunProgress.Finished(report, total));

            if (run.Options.StopOnFailure && report.Status is StepStatus.Failed or StepStatus.Errored)
            {
                stoppedEarly = true;
                break;
            }
        }

        // Everything the run never reached is reported explicitly rather than left out. A report listing
        // 3 of 20 with no sign of the other 17 reads as a run of three.
        foreach (var step in tested.Skip(reports.Count))
        {
            reports.Add(StepReport.SkippedStep(step));
        }

        // Cleanup, after everything else and whatever happened to it. A chain that creates something
        // has to remove it again, and stopOnFailure - the right setting for a chain - guarantees the
        // delete is skipped exactly when it is most needed.
        //
        // NOT after a cancellation: the user asked it to stop, and sending four more requests after
        // Ctrl-C is the opposite of stopping. That leaks, and it is the lesser surprise of the two.
        if (!cancelled)
        {
            foreach (var step in teardown)
            {
                progress?.Report(RunProgress.Starting(step, total));

                StepReport report;
                try
                {
                    report = await RunStepAsync(step, run, profiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Everything left is reported below rather than abandoned, so a half-done cleanup
                    // still says which parts did not happen.
                    cancelled = true;
                    break;
                }

                // No JudgeAsync: comparing cleanup against a snapshot would be comparing something
                // nobody is testing, and a missing snapshot for it would fail the run.
                reports.Add(report);
                progress?.Report(RunProgress.Finished(report, total));
            }

            foreach (var step in teardown.Skip(reports.Count - tested.Count))
            {
                reports.Add(StepReport.SkippedStep(step));
            }
        }
        else
        {
            foreach (var step in teardown)
            {
                reports.Add(StepReport.SkippedStep(step));
            }
        }

        stopwatch.Stop();

        return new RunReport(reports, stopwatch.ElapsedMilliseconds, cancelled, stoppedEarly);
    }

    /// <inheritdoc cref="IEnvironmentPairRunService.RunAsync"/>
    public async Task<EnvironmentPairReport> RunAsync(
        EnvironmentPairRun run,
        IProgress<StepPairProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var leftName = run.Left?.Name ?? "No environment";
        var rightName = run.Right?.Name ?? "No environment";

        if (run.Plan.IsEmpty)
        {
            return EnvironmentPairReport.Empty with { LeftEnvironment = leftName, RightEnvironment = rightName };
        }

        // Read once for the whole run, for the reason given in the single-environment path above - and
        // ONCE for both sides, not once per side: auth profiles are workspace-level, so re-reading them
        // between left and right could only introduce a difference that came from the clock rather than
        // from the environments, which is the one kind of difference this feature must never invent.
        var profiles = await _authProfiles.LoadAuthProfilesAsync(run.Workspace.RootPath, cancellationToken);

        // Bodies are the point of a comparison run, so it asks for them whatever the caller set.
        var options = run.Options with { CaptureResponseBodies = true };
        var left = new CollectionRun(run.Plan, run.Workspace, run.Left, options);
        var right = new CollectionRun(run.Plan, run.Workspace, run.Right, options);

        var stopwatch = Stopwatch.StartNew();
        var pairs = new List<StepPair>(run.Plan.Count);
        var total = run.Plan.Count;
        var cancelled = false;
        var stoppedEarly = false;

        foreach (var step in run.Plan.Steps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            if (run.Options.DelayMilliseconds > 0 && pairs.Count > 0)
            {
                try
                {
                    await Task.Delay(run.Options.DelayMilliseconds, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
            }

            progress?.Report(StepPairProgress.Starting(step, total));

            StepReport leftReport;
            StepReport rightReport;
            try
            {
                leftReport = await RunStepAsync(step, left, profiles, cancellationToken);
                progress?.Report(StepPairProgress.LeftDone(step, total, leftReport));

                rightReport = await RunStepAsync(step, right, profiles, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Skipped on both sides: a pair half-run is not a comparison, and reporting the half
                // that did answer would invite a diff against nothing.
                pairs.Add(new StepPair(step, StepReport.SkippedStep(step), StepReport.SkippedStep(step)));
                cancelled = true;
                break;
            }

            var pair = new StepPair(step, leftReport, rightReport);
            pairs.Add(pair);
            progress?.Report(StepPairProgress.Complete(pair, total));

            // Either side failing stops the run, because the question was about both of them.
            if (run.Options.StopOnFailure &&
                (leftReport.Status is StepStatus.Failed or StepStatus.Errored ||
                 rightReport.Status is StepStatus.Failed or StepStatus.Errored))
            {
                stoppedEarly = true;
                break;
            }
        }

        stopwatch.Stop();

        foreach (var step in run.Plan.Steps.Skip(pairs.Count))
        {
            pairs.Add(new StepPair(step, StepReport.SkippedStep(step), StepReport.SkippedStep(step)));
        }

        return new EnvironmentPairReport(
            leftName, rightName, pairs, stopwatch.ElapsedMilliseconds, cancelled, stoppedEarly);
    }

    /// <summary>
    /// Asks the oracle for the other side and compares, leaving the step's own status alone: whether it
    /// SENT is one question and whether it MATCHES is another, and a step can pass every assertion while
    /// differing from its snapshot (see <see cref="StepReport.Comparison"/>).
    ///
    /// <para>An oracle that wanted a comparison and could not get one is reported as
    /// <see cref="ComparisonVerdict.Unavailable"/>, never as a pass, and never as an error against the
    /// request - the request answered; it is the other side that is missing.</para>
    /// </summary>
    private async Task<StepReport> JudgeAsync(
        StepReport report,
        CollectionRun run,
        CancellationToken cancellationToken)
    {
        if (run.Oracle is not { } oracle || oracle.Kind == OracleKind.None)
        {
            return report;
        }

        var context = new OracleContext(report.Step, run.Workspace, run.Environment, report);

        OtherSide other;
        try
        {
            other = await oracle.ObtainAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return report with
            {
                Comparison = ComparisonVerdict.Unavailable,
                ComparisonUnavailableReason = ex.Message,
            };
        }

        if (other.Unavailable)
        {
            return report with
            {
                Comparison = ComparisonVerdict.Unavailable,
                ComparisonUnavailableReason = other.MissingReason,
            };
        }

        if (!other.Available || report.ResponseBody is not { } body)
        {
            return report;
        }

        try
        {
            var rules = await _settings
                .ResolveRulesAsync(
                    run.Workspace, report.Step.FilePath, report.Step.CaseFilePath, run.Overlay, cancellationToken)
                .ConfigureAwait(false);

            // Both sides through the same redactions and normalisations. A snapshot is stored with
            // "generatedAt": "<timestamp>"; comparing that against a live response carrying the real
            // value would report a difference on every run, and the rule written to stop the churn
            // would cause it. Idempotent, so the already-normalised side is unchanged.
            var left = SnapshotRecorder.ForComparison(other.Body!, rules.Snapshot);
            var right = SnapshotRecorder.ForComparison(body, rules.Snapshot);

            var outcome = await _comparer
                .CompareAsync(left, right, rules.Comparison, cancellationToken)
                .ConfigureAwait(false);

            // Tolerances run on what the comparer FOUND, so the engine stays free of them and one
            // definition of "a difference" still feeds the row, the pane and the report.
            var tolerated = ToleranceEvaluator.Apply(outcome, rules.Tolerances, left, right);

            return report with
            {
                Comparison = tolerated.Outcome.Same ? ComparisonVerdict.Same : ComparisonVerdict.Differs,
                DifferenceCount = tolerated.Outcome.DifferenceCount,
                ToleratedCount = tolerated.Tolerated,
                ComparisonWarnings = tolerated.Warnings,
                ComparedAgainst = other.Source,

                // Both sides as they were COMPARED - normalised, so opening the row shows the same
                // two documents the verdict was reached from rather than two that differ everywhere
                // the rules already excused.
                ResponseBody = right,
                ComparedBody = left,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return report with
            {
                Comparison = ComparisonVerdict.Unavailable,
                ComparisonUnavailableReason = ex.Message,
                ComparedAgainst = other.Source,
            };
        }
    }

    private async Task<StepReport> RunStepAsync(
        RunStep step,
        CollectionRun run,
        IReadOnlyList<AuthProfile> profiles,
        CancellationToken cancellationToken)
    {
        // A batch naming something that is not there. Reported before anything is sent, because there
        // is nothing to send - and reported rather than skipped, so a batch that shrank when an
        // endpoint was renamed cannot keep passing while testing one thing fewer.
        if (step.Unresolved is { Length: > 0 } unresolved)
        {
            return Errored(step, unresolved);
        }

        RequestModel request;
        try
        {
            request = await _requests.LoadRequestAsync(step.FilePath, cancellationToken);

            // A case is folded on here rather than in the plan, for the same reason the request is
            // read here: a run sends what is SAVED, and a plan built ten minutes ago holding loaded
            // documents would send what was saved then.
            if (step.CaseFilePath is { Length: > 0 } casePath)
            {
                request = CaseMerge.Apply(
                    request,
                    await _endpoints.LoadCaseAsync(casePath, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (Exception ex)
        {
            // A request file that will not parse is this step's failure, not the run's. Throwing here
            // would abandon the other nineteen requests over one malformed file, and the report would
            // be an exception instead of the nineteen answers it had already earned.
            return Errored(step, $"Could not read the request: {ex.Message}");
        }

        AuthConfig? effectiveAuth;
        try
        {
            var chain = await _inheritance.GetInheritanceChainAsync(
                run.Workspace.RootPath, step.FilePath, cancellationToken);

            // The chain is BOTH halves of what a folder hands down, not just the auth half. A run
            // that resolved the profile and dropped the headers sent a different request from the one
            // the editor's Send sends - and an environment comparison then reported two systems
            // disagreeing about a request neither of them was actually asked.
            request = EffectiveHeaders.Apply(request, chain);

            var selectedProfile = request.AuthProfileId is { } id
                ? profiles.FirstOrDefault(p => p.Id == id)
                : null;

            effectiveAuth = EffectiveAuthResolver
                .Resolve(request.Auth.Type, request.Auth, selectedProfile, chain, profiles)
                .Config;
        }
        catch (Exception ex)
        {
            return Errored(step, $"Could not resolve what the folders hand down: {ex.Message}");
        }

        try
        {
            var result = await _execution.RunAsync(
                new RequestRun(request, run.Workspace, run.Environment, effectiveAuth, run.Options.RecordHistory),
                cancellationToken);

            // Cleanup is not being tested, so its case's assertions are dropped rather than judged -
            // the same reason the oracle skips it. A batch reusing "delete-cat#created" as teardown
            // reuses a case that expects 204, and on a run where the delete already happened as a
            // STEP the cleanup finds a 404: expected, and reporting it as a failed cleanup on every
            // successful run is exactly the crying wolf teardown exists to avoid.
            //
            // What still counts is whether it could be SENT at all, which is the real leak signal.
            var assertions = step.IsTeardown ? [] : result.Assertions;

            var status = !result.Result.IsSuccess
                ? StepStatus.Errored
                : assertions.Any(a => !a.Passed)
                    ? StepStatus.Failed
                    : StepStatus.Passed;

            // A capture that could not be applied is reported on the step but does NOT fail it: the
            // request answered, and whether a missing field matters is what an assertion is for. It
            // still has to be visible, because the failure it causes usually lands several requests
            // later as a {{variable}} that never resolved.
            return new StepReport(
                step,
                status,
                // StatusCode is a plain int that reads 0 when nothing answered; reporting that would
                // put "0" in the status column of every failed row.
                result.Result.IsSuccess ? result.Result.StatusCode : null,
                result.Result.ReasonPhrase,
                result.Result.ElapsedMilliseconds,
                result.Result.SizeBytes,
                assertions,
                result.Captures,
                result.Result.IsSuccess ? null : result.Result.ErrorMessage)
            {
                ResponseBody = BodyToCarry(run.Options, result.Result, out var tooLarge),
                BodyTooLargeToCompare = tooLarge,
                ContentType = run.Options.CaptureResponseBodies ? result.Result.ContentType : null,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Errored(step, ex.Message);
        }
    }

    /// <summary>
    /// The response body, when the run asked for one and it is small enough to be worth comparing.
    ///
    /// <para>Over the cap it is dropped rather than truncated, and says so: two bodies cut at the same
    /// length look identical past the cut, so a truncated body answers a comparison it cannot see all
    /// of. A failed step carries nothing - the body of a transport error is not a response.</para>
    /// </summary>
    private static string? BodyToCarry(RunOptions options, ExecutionResult result, out bool tooLarge)
    {
        tooLarge = false;
        if (!options.CaptureResponseBodies || !result.IsSuccess)
        {
            return null;
        }

        if (result.Body.Length > StepReport.MaxComparableBodyChars)
        {
            tooLarge = true;
            return null;
        }

        return result.Body;
    }

    private static StepReport Errored(RunStep step, string error) =>
        new(step, StepStatus.Errored, null, null, 0, 0, [], [], error);
}
