using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Application.Running;

/// <summary>What to run, and against what.</summary>
/// <param name="Plan">The ordered steps, already flattened and filtered by <see cref="RunPlan"/>.</param>
public sealed record CollectionRun(
    RunPlan Plan,
    Workspace Workspace,
    WorkspaceEnvironment? Environment,
    RunOptions Options);

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
