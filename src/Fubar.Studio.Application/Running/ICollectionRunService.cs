using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Application.Running;

/// <summary>What to run, and against what.</summary>
/// <param name="Plan">The ordered steps, already flattened and filtered by <see cref="RunPlan"/>.</param>
/// <param name="Oracle">What judges each response. Null is <see cref="NoOracle"/> - assertions only,
/// which is what a plain collection run has always done.</param>
/// <param name="Overlay">The batch's own comparison rules, when this run came from one. Applied after
/// the containment chain resolves, because a batch cuts across the tree rather than sitting in it.</param>
public sealed record CollectionRun(
    RunPlan Plan,
    Workspace Workspace,
    WorkspaceEnvironment? Environment,
    RunOptions Options,
    IOracle? Oracle = null,
    BatchOverlay? Overlay = null);

/// <summary>
/// Sends a collection of requests in order, running each one's captures and assertions, and reports what
/// happened.
///
/// <para><b>Sequential, never parallel, and that is a correctness requirement rather than a simple
/// implementation.</b> Captures write variables that later requests read - the headline case being a
/// login whose token every subsequent request depends on - so two requests in flight at once is a race
/// on the session store whose outcome depends on which response came back first. A "run faster" option
/// here would silently break exactly the collections that are worth running.</para>
///
/// <para>Each step goes through the same <c>IRequestExecutionService</c> a single send does, so auth
/// acquisition, the 401 retry, captures, assertions and history behave identically whether a request is
/// sent by hand or by a run. Anything that works in the editor works in a run, and anything that does
/// not is a real difference rather than a second implementation drifting from the first.</para>
/// </summary>
public interface ICollectionRunService
{
    Task<RunReport> RunAsync(
        CollectionRun run,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>What to run, and against which two environments.</summary>
/// <param name="Left">The reference side, shown on the left of every comparison. Null means "no
/// environment", which is a legitimate thing to compare against.</param>
public sealed record EnvironmentPairRun(
    RunPlan Plan,
    Workspace Workspace,
    WorkspaceEnvironment? Left,
    WorkspaceEnvironment? Right,
    RunOptions Options);

/// <summary>
/// Runs one collection against two environments and pairs the answers, so "does staging still agree
/// with production?" is a single action rather than two runs and a manual comparison.
///
/// <para><b>Interleaved per request - left, right, next request - and not one whole environment then
/// the other.</b> Running A to the end before starting B would leave the first comparable pair until
/// after the last request of A, so a twenty-request collection answers nothing for twenty requests.
/// Interleaved, the first pair is complete after two sends, and rules can be written against it while
/// the rest is still running - which is the whole workflow this exists for.</para>
///
/// <para>That is only safe because everything an environment accumulates is already keyed by it: session
/// variables through <c>SessionScope</c> (<c>workspaceId::environmentId</c>), and the cookie jar and
/// client certificates through the per-(workspace, environment) HTTP client. A token captured against
/// staging cannot be sent to production, whatever order the requests go in. What interleaving must
/// preserve is order WITHIN each environment, since captures chain - and it does: left runs in plan
/// order, right runs in plan order.</para>
/// </summary>
public interface IEnvironmentPairRunService
{
    Task<EnvironmentPairReport> RunAsync(
        EnvironmentPairRun run,
        IProgress<StepPairProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
