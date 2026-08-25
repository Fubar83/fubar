using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Protocols;

/// <summary>
/// The composition seam a new protocol (GraphQL, WebSocket, SSE, gRPC, ...) plugs into. HTTP is
/// registered as just the first implementation - adding another protocol means writing a new
/// <see cref="IProtocolProvider"/> (+ matching <see cref="IRequestExecutor"/>) and registering it
/// in DI, with no changes to HTTP's provider, executor, or any shared UI/service code.
/// </summary>
public interface IProtocolProvider
{
    RequestKind Kind { get; }

    /// <summary>Display name for protocol pickers (e.g. "HTTP", "GraphQL").</summary>
    string DisplayName { get; }

    /// <summary>The verb/method set this protocol's Address Bar should offer (e.g. GET/POST/... for HTTP).</summary>
    IReadOnlyList<string> Methods { get; }

    /// <summary>Builds a blank request of this protocol's kind for the "New Request" flow.</summary>
    RequestModel CreateDefault(string name);
}
