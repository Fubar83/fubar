using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Protocols;

/// <summary>Resolves the registered <see cref="IProtocolProvider"/> for a given <see cref="RequestKind"/>.</summary>
public interface IProtocolRegistry
{
    IProtocolProvider Resolve(RequestKind kind);

    IReadOnlyList<IProtocolProvider> All { get; }
}
