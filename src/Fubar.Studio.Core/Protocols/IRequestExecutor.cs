using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Protocols;

/// <summary>
/// Strategy interface for actually running a request. One implementation per protocol
/// (<c>HttpRequestExecutor</c>, later <c>GraphQlRequestExecutor</c>, ...), resolved via
/// <see cref="IExecutorRegistry"/> by the request's <see cref="RequestModel.Kind"/>.
/// <paramref name="context"/> supplies what <c>IVariableResolver</c> needs to substitute
/// <c>{{variable}}</c> tokens against the caller's active environment before sending.
/// </summary>
public interface IRequestExecutor
{
    RequestKind Kind { get; }

    Task<ExecutionResult> ExecuteAsync(RequestModel request, RequestExecutionContext context, CancellationToken cancellationToken = default);
}
