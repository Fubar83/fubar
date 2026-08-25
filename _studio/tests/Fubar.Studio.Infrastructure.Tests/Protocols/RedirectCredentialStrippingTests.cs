using System.Net;
using System.Net.Http;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Infrastructure.Protocols.Http;

namespace Fubar.Studio.Infrastructure.Tests.Protocols;

/// <summary>The executor follows redirects itself (the scoped client has AllowAutoRedirect = false) so it
/// can drop injected credential headers on a cross-origin hop - .NET only strips <c>Authorization</c>, not
/// custom API-key headers, which would otherwise leak to a redirect target on another host.</summary>
public class RedirectCredentialStrippingTests
{
    private static readonly Workspace Ws = new() { RootPath = "x", Manifest = new AppManifest { Name = "t" } };

    private static RequestModel RequestWithAuthHeaders(string url) => new()
    {
        Name = "r",
        Method = "GET",
        Url = url,
        Headers =
        [
            new KeyValueItem { Key = "Authorization", Value = "Bearer tok" },
            new KeyValueItem { Key = "X-Api-Key", Value = "secret" },
            new KeyValueItem { Key = "X-Trace", Value = "keep" },
        ],
    };

    private static HttpRequestExecutor Executor(RecordingHandler handler) =>
        new(new SingleClientProvider(new HttpClient(handler)), new PassthroughResolver());

    [Fact]
    public async Task Drops_injected_credential_headers_on_a_cross_origin_redirect()
    {
        // a.example 302 -> b.example (different host).
        var handler = new RecordingHandler(uri =>
            uri.Host == "a.example" ? Redirect("https://b.example/landing") : Ok());
        var context = new RequestExecutionContext(Ws, null, ["X-Api-Key"]);

        var result = await Executor(handler).ExecuteAsync(RequestWithAuthHeaders("https://a.example/start"), context);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, handler.Hops.Count);

        // Hop 1 (original origin): every header sent.
        Assert.Contains("Authorization", handler.Hops[0]);
        Assert.Contains("X-Api-Key", handler.Hops[0]);

        // Hop 2 (cross-origin): both credential headers dropped, the non-credential header kept.
        Assert.DoesNotContain("Authorization", handler.Hops[1]);
        Assert.DoesNotContain("X-Api-Key", handler.Hops[1]);
        Assert.Contains("X-Trace", handler.Hops[1]);
    }

    [Fact]
    public async Task Keeps_credential_headers_on_a_same_origin_redirect()
    {
        // a.example/start 302 -> a.example/next (same host).
        var handler = new RecordingHandler(uri =>
            uri.AbsolutePath == "/start" ? Redirect("https://a.example/next") : Ok());
        var context = new RequestExecutionContext(Ws, null, ["X-Api-Key"]);

        var result = await Executor(handler).ExecuteAsync(RequestWithAuthHeaders("https://a.example/start"), context);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, handler.Hops.Count);
        Assert.Contains("Authorization", handler.Hops[1]);
        Assert.Contains("X-Api-Key", handler.Hops[1]);
    }

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent("done") };

    private sealed class RecordingHandler(Func<Uri, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HashSet<string>> Hops { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hops.Add(new HashSet<string>(request.Headers.Select(h => h.Key), StringComparer.OrdinalIgnoreCase));
            return Task.FromResult(responder(request.RequestUri!));
        }
    }

    private sealed class SingleClientProvider(HttpClient client) : IScopedHttpClientProvider
    {
        public HttpClient GetClient(string scope) => client;
    }

    private sealed class PassthroughResolver : IVariableResolver
    {
        public VariableResolution Resolve(string key, Workspace workspace, WorkspaceEnvironment? activeEnvironment) => new(false, "", "");
        public string Substitute(string? input, Workspace workspace, WorkspaceEnvironment? activeEnvironment) => input ?? "";
        public IReadOnlyList<VariableSuggestion> ListAvailable(Workspace workspace, WorkspaceEnvironment? activeEnvironment) => [];
    }
}
