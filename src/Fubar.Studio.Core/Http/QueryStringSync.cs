namespace Fubar.Studio.Core.Http;

/// <summary>
/// Pure helpers for the request editor's bidirectional URL ⇄ query-params sync (RequestEditorPane.md §3):
/// parsing a URL's query string into key/value pairs, and rebuilding a URL from a base plus params. The
/// view model owns the row-reconciliation (identity/enabled state); the string mechanics live here so
/// they're testable without any UI.
/// </summary>
public static class QueryStringSync
{
    /// <summary>Parses the <c>?a=1&amp;b=2</c> portion of <paramref name="url"/> into decoded pairs.
    /// Empty when the URL has no query string.</summary>
    public static IReadOnlyList<(string Key, string Value)> ParseQuery(string url)
    {
        var q = url.IndexOf('?');
        if (q < 0)
        {
            return [];
        }

        return url[(q + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var eq = pair.IndexOf('=');
                var key = eq < 0 ? pair : pair[..eq];
                var value = eq < 0 ? "" : pair[(eq + 1)..];
                return (Uri.UnescapeDataString(key), Uri.UnescapeDataString(value));
            })
            .ToList();
    }

    /// <summary>The part of <paramref name="url"/> before any <c>?</c>.</summary>
    public static string BasePart(string url)
    {
        var q = url.IndexOf('?');
        return q < 0 ? url : url[..q];
    }

    /// <summary>Rebuilds a URL from <paramref name="url"/>'s base plus the given (already-filtered) params,
    /// URL-encoding each key/value. Params with a blank key are dropped.</summary>
    public static string BuildUrl(string url, IEnumerable<(string Key, string Value)> queryParams)
    {
        var basePart = BasePart(url);
        var pairs = queryParams.Where(p => !string.IsNullOrWhiteSpace(p.Key)).ToList();
        return pairs.Count == 0
            ? basePart
            : $"{basePart}?{string.Join('&', pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"))}";
    }
}
