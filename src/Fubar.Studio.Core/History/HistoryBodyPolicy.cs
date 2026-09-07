namespace Fubar.Studio.Core.History;

/// <summary>
/// Decides how much of a response body history is allowed to keep.
///
/// <para>History exists to be compared against, which is useless without the body - but the ledger
/// holds many executions per request and a response has no size limit, so storing every body verbatim
/// turns a workspace into a multi-gigabyte cache of things nobody asked to keep. A body over the cap
/// is dropped entirely rather than truncated: half a JSON document cannot be diffed, and showing a
/// comparison against a silently cut-off body is worse than saying there is nothing to compare.</para>
///
/// <para>The cap is a SETTING rather than a constant, and zero is a meaningful value: it keeps the
/// timing and status of every execution and no payloads at all. That is the option someone working on
/// a machine they do not control needs - a login response body on disk is a token on disk - and it did
/// not exist.</para>
/// </summary>
public static class HistoryBodyPolicy
{
    /// <summary>Default largest response body, in characters, that a snapshot will carry.</summary>
    public const int DefaultMaxResponseBodyChars = 256 * 1024;

    /// <summary>
    /// The body to persist, or <c>null</c> when there is nothing worth keeping - which is exactly the
    /// condition that makes an entry non-comparable.
    /// </summary>
    /// <param name="maxChars">The cap. Zero keeps no body at all.</param>
    public static string? Capture(string? body, int maxChars = DefaultMaxResponseBodyChars) =>
        string.IsNullOrEmpty(body) || maxChars <= 0 || body.Length > maxChars ? null : body;
}
