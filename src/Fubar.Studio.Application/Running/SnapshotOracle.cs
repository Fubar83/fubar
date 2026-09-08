using Fubar.Studio.Core.Snapshots;

namespace Fubar.Studio.Application.Running;

/// <summary>
/// Compares a response against the one recorded for this request in this environment.
/// </summary>
/// <remarks>
/// Reads only. Recording is a separate, deliberate act (<c>ISnapshotRecordingService</c>) rather than
/// something a comparison run does when it finds nothing: a snapshot that writes itself on the first
/// failing run tests nothing ever again, and would do it silently.
/// </remarks>
public sealed class SnapshotOracle : IOracle
{
    private readonly ISnapshotStore _store;

    public SnapshotOracle(ISnapshotStore store)
    {
        _store = store;
    }

    public OracleKind Kind => OracleKind.Snapshot;

    public async Task<OtherSide> ObtainAsync(OracleContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A step that never answered has nothing to compare, and reporting "no snapshot" for it would
        // blame the wrong thing - the failure is that the request did not run.
        if (context.Primary.ResponseBody is null)
        {
            return OtherSide.NotApplicable;
        }

        var lookup = await _store
            .FindAsync(
                context.Workspace.RootPath,
                context.Step.FilePath,
                context.Environment?.Name,
                cancellationToken)
            .ConfigureAwait(false);

        if (lookup is { Snapshot: { } snapshot, Source: { } source })
        {
            return OtherSide.From(snapshot.BodyForComparison(), source);
        }

        return OtherSide.Missing(context.Environment is { } environment
            ? $"No snapshot recorded for {environment.Name}"
            : "No snapshot recorded");
    }
}
