using System.Text;
using Fubar.Studio.Core.Http;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Infrastructure.Import;

/// <summary>
/// Renders a <see cref="RequestModel"/> as a multi-line <c>curl</c> command. The URL already carries its
/// (enabled) query string, so it is emitted verbatim; header values and the body are variable-resolved
/// via the supplied delegate. Single quotes are shell-escaped so pasting is safe.
/// </summary>
public sealed class CurlExporter : ICurlExportService
{
    private const string LineBreak = " \\\n  ";

    public string ToCurl(RequestModel request, Func<string?, string> resolve)
    {
        var sb = new StringBuilder("curl");

        var method = string.IsNullOrWhiteSpace(request.Method) ? "GET" : request.Method.ToUpperInvariant();
        if (method != "GET")
        {
            sb.Append(" -X ").Append(method);
        }

        sb.Append(" '").Append(Escape(resolve(request.Url))).Append('\'');

        foreach (var header in request.Headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            sb.Append(LineBreak).Append("-H '").Append(Escape($"{header.Key}: {resolve(header.Value)}")).Append('\'');
        }

        // The content type the BODY TYPE implies, when nobody stated one. Without it the exported
        // command is not the request: `--data` makes curl send application/x-www-form-urlencoded, so a
        // JSON body copied out of here arrived at the server as a form - and comparing "it works in
        // curl" against "it fails in the app" then compares two different requests, which is the worst
        // possible thing for the copy button to be for.
        if (!request.Headers.Any(h => h.Enabled && string.Equals(h.Key, HttpHeaderNames.ContentType, StringComparison.OrdinalIgnoreCase))
            && ImpliedContentType(request.Body) is { } implied)
        {
            sb.Append(LineBreak).Append("-H '").Append(Escape($"{HttpHeaderNames.ContentType}: {implied}")).Append('\'');
        }

        AppendBody(sb, request.Body, resolve);

        return sb.ToString();
    }

    /// <summary>
    /// What the executor would put on the content for this body, or null where curl already does the
    /// same thing by itself.
    /// </summary>
    /// <remarks>
    /// <c>--data-urlencode</c> sends <c>application/x-www-form-urlencoded</c> and <c>-F</c> sends
    /// <c>multipart/form-data</c> with curl's own boundary, so stating either would add noise and, for
    /// multipart, a boundary that does not match the one curl generates.
    /// </remarks>
    private static string? ImpliedContentType(RequestBody body) => body.Type switch
    {
        BodyType.Json when !string.IsNullOrEmpty(body.Raw) => "application/json",
        BodyType.RawText when !string.IsNullOrEmpty(body.Raw) => "text/plain",
        BodyType.BinaryFile when !string.IsNullOrWhiteSpace(body.BinaryFilePath) => "application/octet-stream",
        _ => null,
    };

    private static void AppendBody(StringBuilder sb, RequestBody body, Func<string?, string> resolve)
    {
        switch (body.Type)
        {
            case BodyType.Json or BodyType.RawText when !string.IsNullOrEmpty(body.Raw):
                sb.Append(LineBreak).Append("--data '").Append(Escape(resolve(body.Raw))).Append('\'');
                break;

            case BodyType.UrlEncoded:
                foreach (var f in body.UrlEncoded.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)))
                {
                    sb.Append(LineBreak).Append("--data-urlencode '").Append(Escape($"{f.Key}={resolve(f.Value)}")).Append('\'');
                }
                break;

            case BodyType.FormData:
                foreach (var f in body.FormData.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)))
                {
                    sb.Append(LineBreak).Append("-F '").Append(Escape($"{f.Key}={resolve(f.Value)}")).Append('\'');
                }
                break;

            case BodyType.BinaryFile when !string.IsNullOrWhiteSpace(body.BinaryFilePath):
                sb.Append(LineBreak).Append("--data-binary '@").Append(Escape(body.BinaryFilePath)).Append('\'');
                break;
        }
    }

    /// <summary>Escapes a value for inclusion in a single-quoted shell argument: <c>'</c> becomes
    /// <c>'\''</c> (close quote, escaped quote, reopen quote).</summary>
    private static string Escape(string value) => value.Replace("'", "'\\''");
}
