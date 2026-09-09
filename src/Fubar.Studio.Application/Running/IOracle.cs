using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Application.Running;

/// <summary>What a run compares its responses against.</summary>
public enum OracleKind
{
    /// <summary>Nothing. Assertions decide the verdict, which is what a plain collection run does.</summary>
    None,

    /// <summary>The stored response for this request in this environment.</summary>
    Snapshot,

    /// <summary>The same request sent to another environment.</summary>
    Environment,
}

/// <summary>One step, and what it just answered.</summary>
public sealed record OracleContext(
    RunStep Step,
    Workspace Workspace,
    WorkspaceEnvironment? Environment,
    StepReport Primary);

/// <summary>
/// The other side of a comparison, or why there isn't one.
/// </summary>
/// <param name="Body">Null when nothing could be obtained.</param>
/// <param name="Source">Where it came from, named for the report - "snapshots/staging.json",
/// "Production". A run that does not say which side it compared against cannot be trusted twice.</param>
/// <param name="MissingReason">Why there is nothing, in the user's words. Never null when
/// <paramref name="Body"/> is, and never a reason to pass.</param>
public sealed record OtherSide(string? Body, string? Source, string? MissingReason)
{
    public static readonly OtherSide NotApplicable = new(null, null, null);

    public static OtherSide From(string body, string source) => new(body, source, null);

    public static OtherSide Missing(string reason) => new(null, null, reason);

    /// <summary>There is something to compare.</summary>
    public bool Available => Body is not null;

    /// <summary>This oracle wanted a comparison and could not get one - as distinct from an oracle
    /// that never compares at all, which is not a failure.</summary>
    public bool Unavailable => Body is null && MissingReason is not null;
}

/// <summary>
/// Obtains the other side of a comparison for one step.
/// </summary>
/// <remarks>
/// <para>
/// The seam that makes regression testing and environment comparison one feature rather than two.
/// Every oracle differs ONLY in where the other side comes from; none of them may bring a second
/// comparison implementation, because judging happens once, in <c>IResponseComparer</c>. That rule is
/// the test for whether this design is still being held to.
/// </para>
/// <para>
/// A missing other side is never a pass. An oracle that cannot answer says why, and the runner turns
/// that into its own verdict rather than into silence - "nothing to compare, therefore fine" is how a
/// suite stops testing without anyone noticing.
/// </para>
/// </remarks>
public interface IOracle
{
    OracleKind Kind { get; }

    Task<OtherSide> ObtainAsync(OracleContext context, CancellationToken cancellationToken = default);
}

/// <summary>Compares against nothing. What a plain run has always done.</summary>
public sealed class NoOracle : IOracle
{
    public static readonly NoOracle Instance = new();

    public OracleKind Kind => OracleKind.None;

    public Task<OtherSide> ObtainAsync(OracleContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(OtherSide.NotApplicable);
}
