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

    /// <summary>
    /// The encoding the body was decoded with, e.g. <c>utf-8</c> - reported so mojibake has an
    /// explanation rather than being a mystery. Null for a result that carries no body.
    /// </summary>
    public string? BodyEncodingName { get; init; }

    /// <summary>
    /// True when the response was larger than the executor's cap and was NOT read.
    ///
    /// <para>Distinct from an empty body on purpose: the status, headers and timing are all real and
    /// worth showing, and an assertion against a body that was never loaded must fail loudly rather
    /// than quietly pass against an empty string.</para>
    /// </summary>
    public bool BodyTooLarge { get; init; }

    public bool IsSuccess => ErrorMessage is null;
}
