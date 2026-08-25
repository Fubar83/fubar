namespace Fubar.Studio.Core.Http;

/// <summary>
/// Common HTTP request-header names offered as autocomplete hints in the Headers editor. Deliberately
/// request-oriented (not response headers) and ordered by how often they're typed by hand, so the
/// empty-prefix dropdown leads with the ones people actually add. Schema-declared header names (from an
/// imported OpenAPI operation) are merged ahead of these per request.
/// </summary>
public static class HttpHeaderNames
{
    public static readonly string[] Common =
    [
        "Accept",
        "Authorization",
        "Content-Type",
        "Accept-Encoding",
        "Accept-Language",
        "Cache-Control",
        "Cookie",
        "User-Agent",
        "Referer",
        "Origin",
        "Host",
        "Connection",
        "If-Match",
        "If-None-Match",
        "If-Modified-Since",
        "Range",
        "X-Requested-With",
        "X-Api-Key",
        "X-Correlation-ID",
        "X-Request-ID",
        "X-Forwarded-For",
        "Prefer",
    ];
}
