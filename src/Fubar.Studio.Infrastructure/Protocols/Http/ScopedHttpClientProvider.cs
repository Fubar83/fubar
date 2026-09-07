using System.Collections.Concurrent;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Infrastructure.Protocols.Http;

/// <summary>Hands out one <see cref="HttpClient"/> per session scope (workspace + environment), each with
/// its own <see cref="System.Net.CookieContainer"/>, so cookie-based sessions are isolated per environment - a login
/// cookie set under DEV is never replayed against PROD.</summary>
public interface IScopedHttpClientProvider
{
    /// <summary>
    /// The client for this scope, built with the environment's transport settings.
    ///
    /// <para><paramref name="transport"/> takes part in the cache key. It has to: two environments
    /// differing only in their client certificate would otherwise share one handler and present the
    /// wrong certificate, which is the same class of leak the per-environment cookie jar exists to
    /// prevent.</para>
    /// </summary>
    HttpClient GetClient(string scope, TransportSettings? transport = null, string? workspaceRootPath = null);

    /// <summary>What could not be set up for the most recent build of this scope's client - a
    /// certificate thumbprint that matched nothing, an unreadable CA file. Empty when all is well.
    /// Surfaced so a failure says which setting is wrong rather than arriving later as a TLS error.</summary>
    IReadOnlyList<string> ProblemsFor(string scope, TransportSettings? transport = null);
}

/// <inheritdoc cref="IScopedHttpClientProvider"/>
public sealed class ScopedHttpClientProvider : IScopedHttpClientProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _byScope = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _problemsByScope = new();

    public HttpClient GetClient(string scope, TransportSettings? transport = null, string? workspaceRootPath = null) =>
        _byScope.GetOrAdd(CacheKey(scope, transport), _ =>
        {
            var (handler, problems) = TransportHandlerFactory.Create(transport, workspaceRootPath);
            _problemsByScope[CacheKey(scope, transport)] = problems.Messages;

            // The executor enforces per-request timeouts via its own linked CancellationTokenSource, so the
            // client's own timeout must not clip a longer per-request timeout.
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        });

    public IReadOnlyList<string> ProblemsFor(string scope, TransportSettings? transport = null) =>
        _problemsByScope.TryGetValue(CacheKey(scope, transport), out var problems) ? problems : [];

    /// <summary>The scope plus a fingerprint of the transport settings, so changing a certificate or a
    /// proxy hands back a new client rather than the one built before the change.</summary>
    private static string CacheKey(string scope, TransportSettings? transport) =>
        transport is null || transport.IsEmpty ? scope : $"{scope}##{transport.Fingerprint()}";

    public void Dispose()
    {
        foreach (var client in _byScope.Values)
        {
            client.Dispose();
        }

        _byScope.Clear();
        _problemsByScope.Clear();
    }
}
