namespace Fubar.Studio.Core.Models;

/// <summary>
/// Protocol discriminator for a request - the seam the Extensibility Architecture is built
/// around. HTTP ships first; GraphQL/WebSocket/SSE/gRPC are added by registering a new
/// <c>IProtocolProvider</c> for the corresponding value, not by editing existing protocol code.
/// Serialized as a lowercase string in <c>request.json</c> (default <c>http</c>).
/// </summary>
public enum RequestKind
{
    Http,
    GraphQl,
    WebSocket,
    Sse,
    Grpc,
}
