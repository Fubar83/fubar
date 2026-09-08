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

    public CollectionRunService(
        IRequestExecutionService execution,
        IRequestStore requests,
        IInheritanceResolver inheritance,
        IAuthProfileStore authProfiles,
        IResponseComparer comparer,
        IRequestComparisonSettings settings)
    {
        _execution = execution;
        _requests = requests;
        _inheritance = inheritance;
        _authProfiles = authProfiles;
        _comparer = comparer;
        _settings = settings;
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

        foreach (var step in run.Plan.Steps)
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

        stopwatch.Stop();

        // Everything the run never reached is reported explicitly rather than left out. A report listing
        // 3 of 20 with no sign of the other 17 reads as a run of three.
        foreach (var step in run.Plan.Steps.Skip(reports.Count))
        {
            reports.Add(StepReport.SkippedStep(step));
        }

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
            var settings = await _settings
                .ResolveAsync(run.Workspace, report.Step.FilePath, cancellationToken)
                .ConfigureAwait(false);

            var outcome = await _comparer
                .CompareAsync(other.Body!, body, settings, cancellationToken)
                .ConfigureAwait(false);

            return report with
            {
                Comparison = outcome.Same ? ComparisonVerdict.Same : ComparisonVerdict.Differs,
                DifferenceCount = outcome.DifferenceCount,
                ComparedAgainst = other.Source,
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
        RequestModel request;
        try
        {
            request = await _requests.LoadRequestAsync(step.FilePath, cancellationToken);
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

            var selectedProfile = request.AuthProfileId is { } id
                ? profiles.FirstOrDefault(p => p.Id == id)
                : null;

            effectiveAuth = EffectiveAuthResolver
                .Resolve(request.Auth.Type, request.Auth, selectedProfile, chain, profiles)
                .Config;
        }
        catch (Exception ex)
        {
            return Errored(step, $"Could not resolve auth: {ex.Message}");
        }

        try
        {
            var result = await _execution.RunAsync(
                new RequestRun(request, run.Workspace, run.Environment, effectiveAuth, run.Options.RecordHistory),
                cancellationToken);

            var status = !result.Result.IsSuccess
                ? StepStatus.Errored
                : result.Assertions.Any(a => !a.Passed)
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
                result.Assertions,
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
