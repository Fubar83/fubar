using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Testing;

namespace Fubar.Studio.Application.Requests;

/// <summary>Inputs for one request run: the assembled request, its workspace/environment, the effective
/// auth to ensure first (null = none), and whether to persist a history entry.</summary>
public sealed record RequestRun(
    RequestModel Request,
    Workspace Workspace,
    WorkspaceEnvironment? Environment,
    AuthConfig? EffectiveAuth,
    bool RecordHistory = true);

/// <summary>Everything a caller needs to update the UI after a run: the raw result, the auth outcome (if
/// auth ran), the assertion/capture results (empty unless the response succeeded), and the persisted
/// history snapshot (or the error that stopped it persisting).</summary>
public sealed record RequestRunResult(
    ExecutionResult Result,
    AuthOutcome? Auth,
    IReadOnlyList<AssertionResult> Assertions,
    IReadOnlyList<CaptureResult> Captures,
    ExecutionSnapshot? HistorySnapshot,
    string? HistoryError);

/// <summary>
/// The "send a request" use case, orchestrating the whole pipeline that previously lived inline in the
/// request editor view model: ensure dynamic auth → execute via the protocol registry → on success, run
/// captures then assertions → record history. Pure application logic (no UI, no view-model types), so it
/// is unit-testable with fakes.
/// </summary>
public interface IRequestExecutionService
{
    Task<RequestRunResult> RunAsync(RequestRun run, CancellationToken cancellationToken = default);
}
