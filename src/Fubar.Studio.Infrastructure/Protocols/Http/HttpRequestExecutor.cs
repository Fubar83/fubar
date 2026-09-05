using System.Diagnostics;
using System.Net;
using System.Text;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Infrastructure.Protocols.Http;

/// <summary>
/// The first (and for now, only) registered <see cref="IRequestExecutor"/>: builds an
/// <see cref="HttpRequestMessage"/> from a <see cref="RequestModel"/>, sends it via an
/// <see cref="IScopedHttpClientProvider"/> client whose cookie jar is scoped per (workspace, environment),
/// and measures timing/size. <c>{{key}}</c> tokens in the URL, headers, and body are substituted via
/// <see cref="IVariableResolver"/> against the caller's <see cref="RequestExecutionContext"/> (active
/// workspace + environment) - see RequestEditorPane.md §1.3, "Environment-Only Variables".
/// </summary>
public sealed class HttpRequestExecutor : IRequestExecutor
{
    /// <summary>Fallback when no setting and no per-request timeout say otherwise.</summary>
    private static readonly TimeSpan FallbackTimeout = TimeSpan.FromSeconds(100);

    private readonly IScopedHttpClientProvider _scopedClients;
    private readonly IVariableResolver _variableResolver;
    private readonly IAppSettingsService? _settings;

    /// <summary>Settings are optional so the many tests that construct this directly need not supply
    /// them; without them the built-in defaults apply, which is what a first run gets anyway.</summary>
    public HttpRequestExecutor(
        IScopedHttpClientProvider scopedClients,
        IVariableResolver variableResolver,
        IAppSettingsService? settings = null)
    {
        _scopedClients = scopedClients;
        _variableResolver = variableResolver;
        _settings = settings;
    }

    public RequestKind Kind => RequestKind.Http;

    public async Task<ExecutionResult> ExecuteAsync(RequestModel request, RequestExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // The request wins, then the user setting, then the fallback. A request that names its own
        // timeout means it, and a global default must not quietly override it.
        var settings = _settings?.Load().Requests;
        var timeout = request.TimeoutSeconds is int s and > 0
            ? TimeSpan.FromSeconds(s)
            : settings?.DefaultTimeoutSeconds is int d and > 0 ? TimeSpan.FromSeconds(d) : FallbackTimeout;
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var url = BuildUrl(request, context);

            // Cookies are isolated per (workspace, environment) - a DEV session cookie is never sent to
            // PROD - and so is the transport configuration, which takes part in the cache key so two
            // environments cannot end up sharing a handler carrying the wrong client certificate.
            var scope = SessionScope.For(context.Workspace, context.ActiveEnvironment);
            var transport = context.ActiveEnvironment?.Transport;
            var client = _scopedClients.GetClient(scope, transport, context.Workspace.RootPath);

            // A thumbprint that matched no certificate, or a CA file that would not load, otherwise
            // arrives much later as a TLS handshake error that names none of them.
            if (_scopedClients.ProblemsFor(scope, transport) is { Count: > 0 } problems)
            {
                return new ExecutionResult
                {
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                    ErrorMessage = string.Join(" ", problems),
                };
            }

            using var response = await SendFollowingRedirectsAsync(client, request, url, context, linked.Token);

            // Bounded. ReadAsByteArrayAsync had no cap and the client's MaxResponseContentBufferSize was
            // left at its default, so a response larger than memory took the whole application down -
            // which needs no hostile server, only a badly paginated endpoint.
            var cap = settings?.MaxResponseMegabytes is int mb and > 0 ? mb * 1024 * 1024 : MaxResponseBytes;
            var (bodyBytes, truncated) = await ReadBodyAsync(response, cap, linked.Token);

            // Decoded by what the response SAID, not by assumption. This was Encoding.UTF8 regardless of
            // charset, so a latin-1 or UTF-16 response rendered as mojibake - and assertions and
            // captures then ran against the mangled text, reporting a difference in data that was fine.
            var encoding = ResolveEncoding(response);
            var body = truncated ? "" : encoding.GetString(bodyBytes);

            var headers = response.Headers
                .Concat(response.Content.Headers)
                .Select(h => new KeyValueItem { Key = h.Key, Value = string.Join(", ", h.Value) })
                .ToList();

            return new ExecutionResult
            {
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
                Headers = headers,
                Body = body,
                BodyBytes = bodyBytes,
                ContentType = response.Content.Headers.ContentType?.MediaType,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                SizeBytes = bodyBytes.Length,
                BodyEncodingName = encoding.WebName,
                BodyTooLarge = truncated,
            };
        }
        // A timeout fires the linked token via timeoutCts while the caller's own token stays unset.
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult
            {
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                ErrorMessage = $"Request timed out after {timeout.TotalSeconds:0.#} s.",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult
            {
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                ErrorMessage = "Request cancelled.",
            };
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
            };
        }
    }

    // Redirects are followed here (the client has AllowAutoRedirect = false) so that injected credential
    // headers can be dropped on a cross-origin hop. .NET's built-in redirect handling strips only the
    // well-known `Authorization` header; a custom API-key header would otherwise be replayed to a redirect
    // target on another host (e.g. a malicious/compromised endpoint that 302s to an attacker), leaking it.
    private const int MaxRedirects = 10;

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        HttpClient client, RequestModel request, string initialUrl, RequestExecutionContext context, CancellationToken cancellationToken)
    {
        var method = new HttpMethod(request.Method);
        var uri = new Uri(initialUrl, UriKind.Absolute);

        // Credential headers that must not cross an origin boundary: whatever the auth prestep injected,
        // plus `Authorization` (the scoped client no longer strips it for us, since we redirect here).
        var sensitive = new HashSet<string>(context.SensitiveHeaderNames ?? [], StringComparer.OrdinalIgnoreCase) { "Authorization" };
        var crossedOrigin = false;

        for (var hop = 0; ; hop++)
        {
            using var httpRequest = new HttpRequestMessage(method, uri);
            foreach (var header in request.Headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
            {
                // Once we've left the original origin, never send the credential headers again.
                if (crossedOrigin && sensitive.Contains(header.Key))
                {
                    continue;
                }

                httpRequest.Headers.TryAddWithoutValidation(header.Key, Resolve(header.Value, context));
            }

            if (!IsBodyless(method))
            {
                httpRequest.Content = await BuildContentAsync(request.Body, context, cancellationToken);
            }

            var response = await client.SendAsync(httpRequest, cancellationToken);

            if (hop >= MaxRedirects || RedirectLocation(response) is not { } location)
            {
                return response;
            }

            var next = new Uri(uri, location); // resolves a relative Location against the current URL
            if (!SameOrigin(uri, next))
            {
                crossedOrigin = true;
            }

            // 301/302/303 turn a non-HEAD request into a bodyless GET; 307/308 preserve method + body.
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                && !IsBodyless(method))
            {
                method = HttpMethod.Get;
            }

            uri = next;
            response.Dispose();
        }
    }

    /// <summary>
    /// Default largest response body read into memory, overridden by the user setting. Generous - this
    /// is a desktop tool and people legitimately
    /// inspect large payloads - but finite, which is the whole point.
    /// </summary>
    public const int MaxResponseBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Reads the body up to <see cref="MaxResponseBytes"/>, reporting whether it stopped early.
    ///
    /// <para>Read one chunk past the cap deliberately: a body of exactly the cap is fine, and the only
    /// way to know a stream is longer is to ask for more than fits.</para>
    /// </summary>
    private static async Task<(byte[] Bytes, bool Truncated)> ReadBodyAsync(
        HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return ([], true);
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }

    /// <summary>
    /// The encoding the response declared, falling back to UTF-8.
    ///
    /// <para>A charset nobody recognises falls back rather than throwing: an unreadable body is a much
    /// smaller problem than a send that fails outright, and the name is reported alongside so the
    /// mojibake has an explanation rather than being a mystery.</para>
    /// </summary>
    private static Encoding ResolveEncoding(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.CharSet is not { Length: > 0 } charset)
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim('"', '\''));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static Uri? RedirectLocation(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
            ? response.Headers.Location
            : null;

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port;

    private static bool IsBodyless(HttpMethod method) =>
        string.Equals(method.Method, "GET", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method.Method, "HEAD", StringComparison.OrdinalIgnoreCase);

    private string BuildUrl(RequestModel request, RequestExecutionContext context)
    {
        var url = Resolve(request.Url, context);

        var enabledParams = request.QueryParams.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
        if (enabledParams.Count == 0)
        {
            return url;
        }

        var separator = url.Contains('?') ? "&" : "?";
        var query = string.Join('&', enabledParams.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(Resolve(p.Value, context))}"));
        return $"{url}{separator}{query}";
    }

    private string Resolve(string? input, RequestExecutionContext context) =>
        _variableResolver.Substitute(input, context.Workspace, context.ActiveEnvironment);

    /// <summary>
    /// Resolves an upload path against the workspace root when it is relative.
    ///
    /// <para>Relative is the shape worth encouraging: a workspace is committed, so a request pointing at
    /// <c>fixtures/avatar.png</c> works on a colleague's machine while <c>C:\Users\me\Desktop\…</c>
    /// does not. Absolute is still honoured - people do upload things from outside the workspace.</para>
    /// </summary>
    private static string ResolveWorkspacePath(string path, RequestExecutionContext context) =>
        Path.IsPathRooted(path) ? path : Path.Combine(context.Workspace.RootPath, path);

    /// <summary>
    /// A content type for an upload, from the extension.
    ///
    /// <para>A guess, and the fallback is the honest one: <c>application/octet-stream</c> is what a
    /// server should assume for bytes of unknown type, and getting this wrong is a much smaller
    /// problem than refusing to send the file.</para>
    /// </summary>
    private static string GuessContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" or ".log" or ".csv" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".zip" => "application/zip",
            ".gz" => "application/gzip",
            _ => "application/octet-stream",
        };

    /// <summary>
    /// Builds the outgoing <see cref="HttpContent"/> for every <see cref="BodyType"/> the Body tab
    /// offers - previously only Json/RawText were handled here, so picking FormData/UrlEncoded/
    /// BinaryFile in the UI silently sent no body at all.
    /// </summary>
    private async Task<HttpContent?> BuildContentAsync(RequestBody body, RequestExecutionContext context, CancellationToken cancellationToken)
    {
        switch (body.Type)
        {
            case BodyType.Json or BodyType.RawText when !string.IsNullOrEmpty(body.Raw):
                var contentType = body.Type == BodyType.Json ? "application/json" : "text/plain";
                return new StringContent(Resolve(body.Raw, context), Encoding.UTF8, contentType);

            case BodyType.FormData:
                var multipart = new MultipartFormDataContent();
                foreach (var field in body.FormData.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)))
                {
                    var resolved = Resolve(field.Value, context);

                    if (field.Kind != FieldKind.File)
                    {
                        multipart.Add(new StringContent(resolved), field.Key);
                        continue;
                    }

                    // A file part, which is the thing multipart exists for and which this could not do:
                    // every field went out as StringContent, so picking FormData and choosing a file
                    // sent its PATH as text.
                    var path = ResolveWorkspacePath(resolved, context);

                    if (!File.Exists(path))
                    {
                        // Names the FIELD as well as the path. A committed request can point at a file
                        // a colleague does not have, and "could not find C:\...\avatar.png" on its own
                        // leaves them hunting for which part of the body asked for it.
                        multipart.Dispose();
                        throw new FileNotFoundException(
                            $"The form field \"{field.Key}\" points at \"{path}\", which does not exist.", path);
                    }

                    var part = new StreamContent(File.OpenRead(path));
                    part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GuessContentType(path));

                    multipart.Add(part, field.Key, Path.GetFileName(path));
                }
                return multipart;

            case BodyType.UrlEncoded:
                var pairs = body.UrlEncoded
                    .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key))
                    .Select(f => new KeyValuePair<string, string>(f.Key, Resolve(f.Value, context)));
                return new FormUrlEncodedContent(pairs);

            case BodyType.BinaryFile when !string.IsNullOrWhiteSpace(body.BinaryFilePath):
                var bytes = await File.ReadAllBytesAsync(body.BinaryFilePath, cancellationToken);
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return fileContent;

            default:
                return null;
        }
    }
}
