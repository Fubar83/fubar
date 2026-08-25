using System.Collections.Concurrent;
using System.Net;

namespace Fubar.Studio.Infrastructure.Protocols.Http;

/// <summary>Hands out one <see cref="HttpClient"/> per session scope (workspace + environment), each with
/// its own <see cref="CookieContainer"/>, so cookie-based sessions are isolated per environment - a login
/// cookie set under DEV is never replayed against PROD.</summary>
public interface IScopedHttpClientProvider
{
    HttpClient GetClient(string scope);
}

/// <inheritdoc cref="IScopedHttpClientProvider"/>
public sealed class ScopedHttpClientProvider : IScopedHttpClientProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _byScope = new();

    public HttpClient GetClient(string scope) => _byScope.GetOrAdd(scope, static _ =>
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            // Redirects are followed manually by HttpRequestExecutor so it can strip injected credential
            // headers on a cross-origin hop (the built-in handler only strips `Authorization`, not custom
            // API-key headers) - see RequestExecutionContext.SensitiveHeaderNames.
            AllowAutoRedirect = false,
        };

        // The executor enforces per-request timeouts via its own linked CancellationTokenSource, so the
        // client's own timeout must not clip a longer per-request timeout.
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    });

    public void Dispose()
    {
        foreach (var client in _byScope.Values)
        {
            client.Dispose();
        }

        _byScope.Clear();
    }
}
