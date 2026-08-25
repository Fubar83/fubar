using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Protocols;

/// <summary>
/// The outcome of running a request through an <see cref="IRequestExecutor"/>: status, headers,
/// body, timing, and payload size for the Response Viewer. <see cref="ErrorMessage"/> is set
/// instead of <see cref="StatusCode"/> when the request couldn't complete (DNS failure, timeout,
/// connection refused, etc.) rather than returning an HTTP-level error status.
/// </summary>
public sealed class ExecutionResult
{
    public int StatusCode { get; init; }

    public string? ReasonPhrase { get; init; }

    public List<KeyValueItem> Headers { get; init; } = [];

    public string Body { get; init; } = "";

    /// <summary>Raw response bytes - needed alongside the decoded <see cref="Body"/> string for the
    /// Response Pane's image Preview view (ResponsePane.md §4.1.E), which can't round-trip through text.</summary>
    public byte[] BodyBytes { get; init; } = [];

    /// <summary>The response's <c>Content-Type</c> header, e.g. <c>"image/png"</c>, used to decide
    /// which Response Pane view (Pretty/Tree/Preview) applies.</summary>
    public string? ContentType { get; init; }

    public long ElapsedMilliseconds { get; init; }

    public long SizeBytes { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsSuccess => ErrorMessage is null;
}
