using System.Text;
using Fubar.Studio.Core.Http;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Infrastructure.Import;

/// <summary>
/// Renders a <see cref="RequestModel"/> as a runnable <c>curl</c> command. The URL already carries its
/// (enabled) query string, so it is emitted verbatim; header values and the body are variable-resolved
/// via the supplied delegate. Single quotes are shell-escaped so pasting is safe.
/// </summary>
/// <remarks>
/// Two shells, because one command cannot serve both - see <see cref="CurlShell"/>. Everything that
/// differs between them is decided once, at the top of <see cref="ToCurl"/>, rather than at each
/// append: a second copy of "which quote, which continuation" is how the two forms would drift.
/// </remarks>
public sealed class CurlExporter : ICurlExportService
{
    private const string PosixLineBreak = " \\\n  ";

    public string ToCurl(RequestModel request, Func<string?, string> resolve, CurlShell shell = CurlShell.Posix)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolve);

        var powershell = shell == CurlShell.PowerShell;

        // One line for PowerShell: \ is not a continuation there, and the parser stops at the first one
        // with "Missing expression after unary operator '--'" - having sent nothing.
        var breakBefore = powershell ? " " : PosixLineBreak;

        // curl.exe, not curl: in Windows PowerShell 5.1 `curl` is an alias for Invoke-WebRequest, which
        // understands none of these flags. Naming the executable costs nothing in PowerShell 7.
        var sb = new StringBuilder(powershell ? "curl.exe" : "curl");

        var method = string.IsNullOrWhiteSpace(request.Method) ? "GET" : request.Method.ToUpperInvariant();
        if (method != "GET")
        {
            sb.Append(" -X ").Append(method);
        }

        sb.Append(' ').Append(Quote(resolve(request.Url), powershell));

        foreach (var header in request.Headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            sb.Append(breakBefore).Append("-H ").Append(Quote($"{header.Key}: {resolve(header.Value)}", powershell));
        }

        // The content type the BODY TYPE implies, when nobody stated one. Without it the exported
        // command is not the request: `--data` makes curl send application/x-www-form-urlencoded, so a
        // JSON body copied out of here arrived at the server as a form - and comparing "it works in
        // curl" against "it fails in the app" then compares two different requests, which is the worst
        // possible thing for the copy button to be for.
        if (!request.Headers.Any(h => h.Enabled && string.Equals(h.Key, HttpHeaderNames.ContentType, StringComparison.OrdinalIgnoreCase))
            && ImpliedContentType(request.Body) is { } implied)
        {
            sb.Append(breakBefore).Append("-H ").Append(Quote($"{HttpHeaderNames.ContentType}: {implied}", powershell));
        }

        AppendBody(sb, request.Body, resolve, breakBefore, powershell);

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

    private static void AppendBody(
        StringBuilder sb, RequestBody body, Func<string?, string> resolve, string breakBefore, bool powershell)
    {
        switch (body.Type)
        {
            case BodyType.Json or BodyType.RawText when !string.IsNullOrEmpty(body.Raw):
                sb.Append(breakBefore).Append("--data ").Append(Quote(resolve(body.Raw), powershell));
                break;

            case BodyType.UrlEncoded:
                foreach (var f in body.UrlEncoded.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)))
                {
                    sb.Append(breakBefore).Append("--data-urlencode ").Append(Quote($"{f.Key}={resolve(f.Value)}", powershell));
                }
                break;

            case BodyType.FormData:
                foreach (var f in body.FormData.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)))
                {
                    sb.Append(breakBefore).Append("-F ").Append(Quote($"{f.Key}={resolve(f.Value)}", powershell));
                }
                break;

            case BodyType.BinaryFile when !string.IsNullOrWhiteSpace(body.BinaryFilePath):
                sb.Append(breakBefore).Append("--data-binary ").Append(Quote("@" + body.BinaryFilePath, powershell));
                break;
        }
    }

    /// <summary>
    /// One argument, single-quoted for the shell it is going to.
    /// </summary>
    /// <remarks>
    /// Both shells treat <c>'...'</c> as a literal string and neither expands anything inside it, so
    /// the quote character is the same and only the ESCAPE differs: POSIX ends the string, adds an
    /// escaped quote and reopens it (<c>'\''</c>), while PowerShell doubles the quote (<c>''</c>).
    /// Writing the POSIX form into a PowerShell command corrupts the value silently - the apostrophe in
    /// a name is enough.
    /// </remarks>
    private static string Quote(string value, bool powershell) =>
        powershell
            ? $"'{value.Replace("'", "''", StringComparison.Ordinal)}'"
            : $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
