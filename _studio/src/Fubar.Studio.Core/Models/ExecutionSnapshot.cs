namespace Fubar.Studio.Core.Models;

/// <summary>
/// One historical execution of a request, appended to <c>.fubar/history/&lt;requestId&gt;.json</c>
/// by <c>IHistoryService</c> (RequestEditorPane.md §6). Captures the exact outgoing payload alongside
/// the outcome, so <c>ReplayHistoryCommand</c> can re-send precisely what was sent before while
/// still re-resolving <c>{{variable}}</c> tokens against whatever environment is active *now*.
/// </summary>
public sealed class ExecutionSnapshot
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    // Outgoing payload, as it was sent.
    public string Method { get; set; } = "GET";

    public string Url { get; set; } = "";

    public List<KeyValueItem> Headers { get; set; } = [];

    public string? Body { get; set; }

    // Outcome.
    public int StatusCode { get; set; }

    public string? ReasonPhrase { get; set; }

    public long ElapsedMilliseconds { get; set; }

    public long SizeBytes { get; set; }

    public string? ErrorMessage { get; set; }
}
