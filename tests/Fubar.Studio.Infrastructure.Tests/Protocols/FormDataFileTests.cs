using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Infrastructure.Protocols.Http;

namespace Fubar.Studio.Infrastructure.Tests.Protocols;

/// <summary>
/// The Body tab has always offered multipart/form-data and never been able to attach a file: every
/// field went out as StringContent, so choosing a file sent its PATH as text.
/// </summary>
public class FormDataFileTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(
        Path.GetTempPath(), "fubar-upload-" + Guid.NewGuid().ToString("n"));

    public FormDataFileTests() => Directory.CreateDirectory(Path.Combine(_workspaceRoot, "fixtures"));

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static Workspace WorkspaceAt(string root) => new()
    {
        RootPath = root,
        Manifest = new AppManifest { Id = "ws1", Name = "t" },
    };

    /// <summary>One captured multipart part, read while the request was still alive.</summary>
    private sealed record Part(string? Name, string? FileName, string? ContentType, byte[] Bytes)
    {
        public string Text => System.Text.Encoding.UTF8.GetString(Bytes);
    }

    /// <summary>
    /// Sends the request through a stub handler and hands back the parts that actually went out.
    ///
    /// <para>Read INSIDE the handler, not from a captured request afterwards: the executor wraps the
    /// HttpRequestMessage in a using, so by the time the call returns its content is disposed and every
    /// part reads as empty - which is exactly how the first version of this test failed.</para>
    /// </summary>
    private async Task<IReadOnlyList<Part>> CaptureSentAsync(RequestModel request)
    {
        var handler = new CapturingHandler();
        var executor = new HttpRequestExecutor(new SingleClient(new HttpClient(handler)), new PassthroughResolver());

        await executor.ExecuteAsync(request, new RequestExecutionContext(WorkspaceAt(_workspaceRoot), null));

        return handler.Parts;
    }

    [Fact]
    public async Task A_form_file_field_is_sent_as_a_file_part()
    {
        var file = Path.Combine(_workspaceRoot, "fixtures", "avatar.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);

        var request = new RequestModel
        {
            Name = "upload",
            Method = "POST",
            Url = "https://example.com/upload",
            Body = new RequestBody
            {
                Type = BodyType.FormData,
                FormData =
                [
                    new KeyValueItem { Key = "caption", Value = "a picture" },
                    new KeyValueItem { Key = "avatar", Value = "fixtures/avatar.png", Kind = FieldKind.File },
                ],
            },
        };

        var parts = await CaptureSentAsync(request);

        Assert.Equal(2, parts.Count);

        // The text field stays a text field.
        Assert.Equal("a picture", parts[0].Text);
        Assert.Null(parts[0].FileName);

        // The file part carries the BYTES, a filename, and a guessed content type.
        Assert.Equal([1, 2, 3, 4], parts[1].Bytes);
        Assert.Equal("image/png", parts[1].ContentType);
        Assert.Equal("avatar.png", parts[1].FileName);
    }

    /// <summary>
    /// Relative to the workspace root, so a committed request works on a colleague's machine - which
    /// an absolute Desktop path would not.
    /// </summary>
    [Fact]
    public async Task A_relative_path_resolves_against_the_workspace()
    {
        var file = Path.Combine(_workspaceRoot, "fixtures", "data.json");
        await File.WriteAllTextAsync(file, "{}");

        var request = UploadOf("fixtures/data.json");

        var part = Assert.Single(await CaptureSentAsync(request));

        Assert.Equal("application/json", part.ContentType);
    }

    [Fact]
    public async Task An_absolute_path_is_honoured()
    {
        var file = Path.Combine(_workspaceRoot, "elsewhere.txt");
        await File.WriteAllTextAsync(file, "hello");

        var part = Assert.Single(await CaptureSentAsync(UploadOf(file)));

        Assert.Equal("hello", part.Text);
    }

    /// <summary>Naming the field as well as the path: a committed request can point at a file a
    /// colleague does not have, and the path alone leaves them hunting for which part asked for it.</summary>
    [Fact]
    public async Task A_missing_file_is_reported_with_the_field_name()
    {
        var executor = new HttpRequestExecutor(
            new SingleClient(new HttpClient(new CapturingHandler())), new PassthroughResolver());

        var result = await executor.ExecuteAsync(
            UploadOf("fixtures/not-here.png"),
            new RequestExecutionContext(WorkspaceAt(_workspaceRoot), null));

        Assert.False(result.IsSuccess);
        Assert.Contains("avatar", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("not-here.png", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>An unknown extension is bytes, which is what a server should assume - much smaller a
    /// problem than refusing to send the file.</summary>
    [Fact]
    public async Task An_unknown_extension_falls_back_to_octet_stream()
    {
        var file = Path.Combine(_workspaceRoot, "thing.qqq");
        await File.WriteAllTextAsync(file, "x");

        var part = Assert.Single(await CaptureSentAsync(UploadOf(file)));

        Assert.Equal("application/octet-stream", part.ContentType);
    }

    private static RequestModel UploadOf(string path) => new()
    {
        Name = "upload",
        Method = "POST",
        Url = "https://example.com/upload",
        Body = new RequestBody
        {
            Type = BodyType.FormData,
            FormData = [new KeyValueItem { Key = "avatar", Value = path, Kind = FieldKind.File }],
        },
    };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<Part> Parts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    Parts.Add(new Part(
                        part.Headers.ContentDisposition?.Name?.Trim('"'),
                        part.Headers.ContentDisposition?.FileName?.Trim('"'),
                        part.Headers.ContentType?.MediaType,
                        await part.ReadAsByteArrayAsync(cancellationToken)));
                }
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("") };
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
}
