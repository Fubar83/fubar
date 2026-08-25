using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;

namespace Fubar.Studio.Infrastructure.Protocols;

/// <summary>
/// Collects every DI-registered <see cref="IRequestExecutor"/> and resolves by kind, mirroring
/// <see cref="ProtocolRegistry"/>. Adding a new protocol's executor is one more registration -
/// this class never changes.
/// </summary>
public sealed class ExecutorRegistry : IExecutorRegistry
{
    private readonly Dictionary<RequestKind, IRequestExecutor> _executors;

    public ExecutorRegistry(IEnumerable<IRequestExecutor> executors)
    {
        _executors = executors.ToDictionary(e => e.Kind);
    }

    public IRequestExecutor Resolve(RequestKind kind) =>
        _executors.TryGetValue(kind, out var executor)
            ? executor
            : throw new InvalidOperationException($"No IRequestExecutor is registered for {kind}.");
}
