using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;

namespace Fubar.Studio.Infrastructure.Protocols.Http;

/// <summary>The first (and for now, only) registered <see cref="IProtocolProvider"/>.</summary>
public sealed class HttpProtocolProvider : IProtocolProvider
{
    public RequestKind Kind => RequestKind.Http;

    public string DisplayName => "HTTP";

    public IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];

    public RequestModel CreateDefault(string name) => new()
    {
        Name = name,
        Kind = RequestKind.Http,
        Method = "GET",
    };
}
