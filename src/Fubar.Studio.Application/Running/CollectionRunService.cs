using System.Diagnostics;
using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Running;

/// <inheritdoc cref="ICollectionRunService"/>
public sealed class CollectionRunService : ICollectionRunService
{
    private readonly IRequestExecutionService _execution;
    private readonly IRequestStore _requests;
    private readonly IInheritanceResolver _inheritance;
    private readonly IAuthProfileStore _authProfiles;

    public CollectionRunService(
        IRequestExecutionService execution,
        IRequestStore requests,
        IInheritanceResolver inheritance,
        IAuthProfileStore authProfiles)
    {
        _execution = execution;
        _requests = requests;
        _inheritance = inheritance;
        _authProfiles = authProfiles;
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
                result.Result.IsSuccess ? null : result.Result.ErrorMessage);
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

    private static StepReport Errored(RunStep step, string error) =>
        new(step, StepStatus.Errored, null, null, 0, 0, [], [], error);
}
