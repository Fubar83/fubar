using System.Diagnostics;
using System.Net;
using System.Text;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
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
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    private readonly IScopedHttpClientProvider _scopedClients;
    private readonly IVariableResolver _variableResolver;

    public HttpRequestExecutor(IScopedHttpClientProvider scopedClients, IVariableResolver variableResolver)
    {
        _scopedClients = scopedClients;
        _variableResolver = variableResolver;
    }

    public RequestKind Kind => RequestKind.Http;

    public async Task<ExecutionResult> ExecuteAsync(RequestModel request, RequestExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var timeout = request.TimeoutSeconds is int s and > 0 ? TimeSpan.FromSeconds(s) : DefaultTimeout;
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var url = BuildUrl(request, context);

            // Cookies are isolated per (workspace, environment) - a DEV session cookie is never sent to PROD.
            var client = _scopedClients.GetClient(SessionScope.For(context.Workspace, context.ActiveEnvironment));
            using var response = await SendFollowingRedirectsAsync(client, request, url, context, linked.Token);
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(linked.Token);
            var body = Encoding.UTF8.GetString(bodyBytes);

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
                    multipart.Add(new StringContent(Resolve(field.Value, context)), field.Key);
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
