using System.Text;
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

        AppendBody(sb, request.Body, resolve);

        return sb.ToString();
    }

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
