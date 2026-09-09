namespace Fubar.Studio.Core.Snapshots;

/// <summary>Which file answered, so a run can say so. See <see cref="ISnapshotStore.FindAsync"/>.</summary>
/// <param name="Snapshot">Null when there is none to compare against.</param>
/// <param name="Source">The file it came from, relative and short enough for a report row.</param>
public sealed record SnapshotLookup(ResponseSnapshot? Snapshot, string? Source)
{
    public static readonly SnapshotLookup None = new(null, null);

    public bool Found => Snapshot is not null;
}

/// <summary>
/// Where recorded responses live.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is scoped when it is recorded (<see cref="SnapshotScope"/>), and the lookup is
/// specific-beats-general: the environment's own file, then the shared one, then nothing. That choice
/// is invisible in a green result, which is why <see cref="SnapshotLookup.Source"/> exists and why
/// every step reports which file it used - a run that quietly switched from the shared snapshot to a
/// per-environment one recorded last week is a run whose green means something different.
/// </para>
/// <para>
/// A missing snapshot is <b>never</b> a pass. It is reported as its own verdict, because "nothing to
/// compare, therefore fine" is how a suite silently stops testing.
/// </para>
/// </remarks>
public interface ISnapshotStore
{
    /// <summary>The snapshot that applies to this request in this environment, and which file it was.</summary>
    Task<SnapshotLookup> FindAsync(
        string workspaceRoot,
        string requestPath,
        string? environmentName,
        CancellationToken cancellationToken = default);

    /// <summary>Writes one, at the scope the snapshot itself declares.</summary>
    Task SaveAsync(
        string workspaceRoot,
        string requestPath,
        ResponseSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>Every environment a snapshot exists for, plus whether a shared one does - what the
    /// record dialog needs to say "this will stop 3 environments using the shared snapshot".</summary>
    Task<IReadOnlyList<string>> ScopesAsync(
        string workspaceRoot,
        string requestPath,
        CancellationToken cancellationToken = default);
}
