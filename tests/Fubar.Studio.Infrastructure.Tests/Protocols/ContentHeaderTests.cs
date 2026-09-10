using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Infrastructure.Protocols.Http;

namespace Fubar.Studio.Infrastructure.Tests.Protocols;

/// <summary>
/// Headers the user typed, on the wire.
///
/// <para><c>Content-Type</c> is a CONTENT header, and <c>HttpRequestMessage.Headers</c> is the request
/// header collection: <c>TryAddWithoutValidation</c> returns false for it and adds nothing. It returns
/// a bool nobody was reading, so a typed <c>Content-Type</c> was dropped in silence and the body's own
/// hard-coded one went out instead - <c>application/json</c> for a Json body whatever the user asked
/// for. An API that publishes a versioned media type
/// (<c>application/vnd.acme.order+json;version=2</c>) answers that with <b>415 Unsupported Media
/// Type</b>, and the request pane shows a header that was never sent.</para>
///
/// <para>The same is true of every other content header - <c>Content-Disposition</c>,
/// <c>Content-Language</c>, <c>Content-Encoding</c> - so the fix is about where a header BELONGS
/// rather than about one name.</para>
/// </summary>
public class ContentHeaderTests
{
    private sealed record Sent(string? ContentType, IReadOnlyList<string> ContentTypeValues, string? Accept, string? Language);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Sent? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var contentTypes = request.Content?.Headers.TryGetValues("Content-Type", out var values) == true
                ? values.ToList()
                : [];

            Request = new Sent(
                request.Content?.Headers.ContentType?.ToString(),
                contentTypes,
                request.Headers.Accept.ToString() is { Length: > 0 } a ? a : null,
                request.Content?.Headers.ContentLanguage.FirstOrDefault());

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(""),
            });
        }
    }

    private sealed class SingleClient(HttpClient client) : IScopedHttpClientProvider
    {
        public HttpClient GetClient(string scope, TransportSettings? transport = null, string? workspaceRootPath = null) => client;

        public IReadOnlyList<string> ProblemsFor(string scope, TransportSettings? transport = null) => [];
    }

    private sealed class PassthroughResolver : IVariableResolver
    {
        public VariableResolution Resolve(string key, Workspace workspace, WorkspaceEnvironment? activeEnvironment) => new(false, "", "");

        public string Substitute(string? input, Workspace workspace, WorkspaceEnvironment? activeEnvironment) => input ?? "";

        public IReadOnlyList<VariableSuggestion> ListAvailable(Workspace workspace, WorkspaceEnvironment? activeEnvironment) => [];
    }

    private static readonly Workspace Ws = new()
    {
        RootPath = Path.GetTempPath(),
        Manifest = new AppManifest { Id = "ws1", Name = "t" },
    };

    private static async Task<Sent> SendAsync(RequestModel request)
    {
        var handler = new CapturingHandler();
        var executor = new HttpRequestExecutor(new SingleClient(new HttpClient(handler)), new PassthroughResolver());

        await executor.ExecuteAsync(request, new RequestExecutionContext(Ws, null));

        return handler.Request!;
    }

    private static RequestModel Post(BodyType type, string raw, params (string Key, string Value)[] headers) => new()
    {
        Name = "r",
        Method = "POST",
        Url = "https://example.com/orders",
        Headers = [.. headers.Select(h => new KeyValueItem { Key = h.Key, Value = h.Value, Enabled = true })],
        Body = new RequestBody { Type = type, Raw = raw },
    };

    [Fact]
    public async Task A_typed_Content_Type_is_what_goes_out()
    {
        // The reported symptom: 415 from an endpoint that publishes its own media type, with the pane
        // showing the header the user set and the wire carrying application/json.
        var sent = await SendAsync(Post(
            BodyType.Json,
            """{"id":1}""",
            ("Content-Type", "application/vnd.acme.order+json;version=2")));

        Assert.Equal("application/vnd.acme.order+json; version=2", sent.ContentType);
    }

    [Fact]
    public async Task It_REPLACES_the_body_types_own_rather_than_joining_it()
    {
        // Content headers hold lists. Adding without removing first sends
        // "application/json, application/vnd.acme.order+json" - which is not a media type at all, and
        // fails in a way that looks nothing like the cause.
        var sent = await SendAsync(Post(
            BodyType.Json,
            """{"id":1}""",
            ("Content-Type", "application/vnd.acme.order+json")));

        Assert.Single(sent.ContentTypeValues);
    }

    [Fact]
    public async Task The_body_type_still_decides_when_nobody_says_otherwise()
    {
        var sent = await SendAsync(Post(BodyType.Json, """{"id":1}"""));

        Assert.Equal("application/json; charset=utf-8", sent.ContentType);
    }

    [Fact]
    public async Task A_charset_the_user_states_is_kept()
    {
        // StringContent writes UTF-8 and says so. Someone who states a different charset means it.
        var sent = await SendAsync(Post(
            BodyType.RawText,
            "hello",
            ("Content-Type", "text/plain; charset=iso-8859-1")));

        Assert.Equal("text/plain; charset=iso-8859-1", sent.ContentType);
    }

    [Fact]
    public async Task Header_names_are_matched_however_they_are_capitalised()
    {
        var sent = await SendAsync(Post(
            BodyType.Json,
            """{"id":1}""",
            ("content-type", "application/vnd.acme.order+json")));

        Assert.Equal("application/vnd.acme.order+json", sent.ContentType);
    }

    [Fact]
    public async Task Any_other_content_header_lands_on_the_content_too()
    {
        var sent = await SendAsync(Post(
            BodyType.Json,
            """{"id":1}""",
            ("Content-Language", "nb-NO")));

        Assert.Equal("nb-NO", sent.Language);
    }

    [Fact]
    public async Task A_request_header_is_still_a_request_header()
    {
        // Accept was never broken - it belongs where it was being put - and the fix must not move it.
        var sent = await SendAsync(Post(
            BodyType.Json,
            """{"id":1}""",
            ("Accept", "application/vnd.acme.order+json"),
            ("Content-Type", "application/vnd.acme.order+json")));

        Assert.Equal("application/vnd.acme.order+json", sent.Accept);
        Assert.Equal("application/vnd.acme.order+json", sent.ContentType);
    }

    [Fact]
    public async Task A_form_body_keeps_the_boundary_it_was_built_with()
    {
        // The boundary is generated per request and nobody types it, so replacing a multipart
        // Content-Type wholesale would leave the server unable to find where the parts begin - a
        // "fix" that turns 415 into 400.
        var request = new RequestModel
        {
            Name = "r",
            Method = "POST",
            Url = "https://example.com/orders",
            Headers = [new KeyValueItem { Key = "Content-Type", Value = "multipart/related", Enabled = true }],
            Body = new RequestBody
            {
                Type = BodyType.FormData,
                FormData = [new KeyValueItem { Key = "note", Value = "hello", Enabled = true }],
            },
        };

        var sent = await SendAsync(request);

        Assert.StartsWith("multipart/related", sent.ContentType!, StringComparison.Ordinal);
        Assert.Contains("boundary=", sent.ContentType!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_header_changes_nothing()
    {
        var request = Post(BodyType.Json, """{"id":1}""");
        request.Headers.Add(new KeyValueItem
        {
            Key = "Content-Type",
            Value = "application/vnd.acme.order+json",
            Enabled = false,
        });

        var sent = await SendAsync(request);

        Assert.Equal("application/json; charset=utf-8", sent.ContentType);
    }
}
