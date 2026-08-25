using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;

namespace Fubar.Studio.Infrastructure.Protocols;

/// <summary>
/// Collects every DI-registered <see cref="IProtocolProvider"/> and resolves by kind. Adding a
/// new protocol is one more <c>IProtocolProvider</c> registration - this class never changes.
/// </summary>
public sealed class ProtocolRegistry : IProtocolRegistry
{
    private readonly Dictionary<RequestKind, IProtocolProvider> _providers;

    public ProtocolRegistry(IEnumerable<IProtocolProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Kind);
        All = [.. _providers.Values];
    }

    public IReadOnlyList<IProtocolProvider> All { get; }

    public IProtocolProvider Resolve(RequestKind kind) =>
        _providers.TryGetValue(kind, out var provider)
            ? provider
            : throw new InvalidOperationException($"No IProtocolProvider is registered for {kind}.");
}
