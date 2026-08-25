using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Protocols;

/// <summary>Resolves the registered <see cref="IRequestExecutor"/> for a given <see cref="RequestKind"/>.</summary>
public interface IExecutorRegistry
{
    IRequestExecutor Resolve(RequestKind kind);
}
