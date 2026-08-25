namespace Fubar.Studio.Core.History;

/// <summary>
/// Decides how much of a response body history is allowed to keep.
///
/// History exists to be compared against, which is useless without the body - but the ledger holds up
/// to 200 executions per request and a response has no size limit, so storing every body verbatim
/// turns a workspace into a multi-gigabyte cache of things nobody asked to keep. A body over the cap
/// is dropped entirely rather than truncated: half a JSON document cannot be diffed, and showing the
/// user a comparison against a silently cut-off body is worse than telling them there is nothing to
/// compare.
/// </summary>
public static class HistoryBodyPolicy
{
    /// <summary>Largest response body, in characters, that a snapshot will carry.</summary>
    public const int MaxResponseBodyChars = 256 * 1024;

    /// <summary>
    /// The body to persist, or <c>null</c> when there is nothing worth keeping - which is exactly the
    /// condition that makes an entry non-comparable.
    /// </summary>
    public static string? Capture(string? body) =>
        string.IsNullOrEmpty(body) || body.Length > MaxResponseBodyChars ? null : body;
}
