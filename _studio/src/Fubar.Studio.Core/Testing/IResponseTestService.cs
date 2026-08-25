using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;

namespace Fubar.Studio.Core.Testing;

/// <summary>
/// Runs a request's post-response <see cref="Assertion"/>s and <see cref="CaptureRule"/>s against an
/// <see cref="ExecutionResult"/>. Assertions are pure (no side effects); captures write to the session
/// store or the active environment, which is why applying them is async and workspace-scoped.
/// </summary>
public interface IResponseTestService
{
    IReadOnlyList<AssertionResult> RunAssertions(IReadOnlyList<Assertion> assertions, ExecutionResult result);

    Task<IReadOnlyList<CaptureResult>> ApplyCapturesAsync(
        IReadOnlyList<CaptureRule> captures,
        ExecutionResult result,
        Workspace workspace,
        WorkspaceEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default);
}
