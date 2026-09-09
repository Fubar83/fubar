using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Snapshots;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Running;

/// <param name="Scope">Per-environment or shared. Chosen when saving, not configured in advance -
/// per-environment by default, because its failure mode is a redundant file while a shared snapshot
/// across environments holding different data reports a data difference as a regression, and a
/// regression tool that cries wolf stops being run.</param>
public sealed record SnapshotRecording(
    RunPlan Plan,
    Workspace Workspace,
    WorkspaceEnvironment? Environment,
    SnapshotScope Scope,
    RunOptions Options);

/// <param name="Warnings">Rules that could not be applied - a path that will not parse, or a response
/// that turned out not to be JSON. Reported rather than swallowed: a redaction that silently did
/// nothing is the worst failure this feature has.</param>
public sealed record SnapshotRecordingReport(
    RunReport Run,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Warnings);

/// <summary>Records the responses a plan produces, as the snapshots later runs compare against.</summary>
public interface ISnapshotRecordingService
{
    Task<SnapshotRecordingReport> RecordAsync(
        SnapshotRecording recording,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISnapshotRecordingService"/>
/// <remarks>
/// A separate, deliberate act rather than something a comparison run does when it finds nothing
/// recorded. A snapshot that writes itself on the first failing run tests nothing ever again, and
/// does it silently - so recording is always something someone asked for.
/// </remarks>
public sealed class SnapshotRecordingService : ISnapshotRecordingService
{
    private readonly ICollectionRunService _runner;
    private readonly ISnapshotStore _store;
    private readonly IRequestComparisonSettings _rules;

    public SnapshotRecordingService(
        ICollectionRunService runner,
        ISnapshotStore store,
        IRequestComparisonSettings rules)
    {
        _runner = runner;
        _store = store;

        // The SAME resolution the run uses to judge. Two walks down the same chain would be two
        // chances to disagree about what gets redacted - and a snapshot written with one set of rules
        // and compared with another differs on every run.
        _rules = rules;
    }

    public async Task<SnapshotRecordingReport> RecordAsync(
        SnapshotRecording recording,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);

        // Bodies are the entire point, so it asks for them whatever the caller set - and no oracle,
        // because recording is not a comparison.
        var report = await _runner
            .RunAsync(
                new CollectionRun(
                    recording.Plan,
                    recording.Workspace,
                    recording.Environment,
                    recording.Options with { CaptureResponseBodies = true }),
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        var written = new List<string>();
        var warnings = new List<string>();

        foreach (var step in report.Steps)
        {
            // Cleanup is not being tested, so there is nothing to record about it - the same reason
            // the oracle skips it. Not a tidiness point: a batch reusing "delete-cat#created" as
            // teardown reuses the case a STEP also ran, so recording both wrote the cleanup's 404
            // over the step's 204 and every later run reported the step as a regression.
            if (step.Step.IsTeardown)
            {
                continue;
            }

            if (step.ResponseBody is not { } body)
            {
                // Only a response can be recorded. A step that errored, was skipped, or whose body was
                // too large is reported rather than written as an empty snapshot every later run would
                // then compare against.
                if (step.Status is not StepStatus.Skipped)
                {
                    warnings.Add($"{step.Step.QualifiedName}: nothing recorded ({Reason(step)})");
                }

                continue;
            }

            var policy = (await _rules
                .ResolveRulesAsync(
                    recording.Workspace, step.Step.FilePath, step.Step.CaseFilePath, null, cancellationToken)
                .ConfigureAwait(false))
                .Snapshot;

            var result = SnapshotRecorder.Record(
                body,
                step.StatusCode ?? 0,
                Headers(step),
                recording.Scope == SnapshotScope.Shared ? null : recording.Environment?.Name,
                policy,
                step.Step.CaseName,
                recordedBy: "fubar");

            foreach (var path in result.UnsupportedPaths)
            {
                warnings.Add($"{step.Step.QualifiedName}: rule \"{path}\" could not be applied");
            }

            await _store
                .SaveAsync(recording.Workspace.RootPath, step.Step.SubjectPath, result.Snapshot, cancellationToken)
                .ConfigureAwait(false);

            written.Add(step.Step.QualifiedName);
        }

        return new SnapshotRecordingReport(report, written, warnings);
    }

    private static string Reason(StepReport step) =>
        step.BodyTooLargeToCompare ? "response too large"
            : step.Error is { Length: > 0 } error ? error
            : "no response body";

    /// <summary>Response headers are not on the report yet, so nothing is kept until they are - better
    /// an empty header set than a snapshot claiming to record headers it never saw.</summary>
    private static Dictionary<string, string> Headers(StepReport step) =>
        step.ContentType is { Length: > 0 } contentType
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = contentType }
            : [];

}
