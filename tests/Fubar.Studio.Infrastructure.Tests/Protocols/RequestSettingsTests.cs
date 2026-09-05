using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Infrastructure.Protocols.Http;

namespace Fubar.Studio.Infrastructure.Tests.Protocols;

/// <summary>
/// The two send limits that used to be constants: how long a request waits, and how much of a response
/// is read into memory. Both are settings now, and a setting that does not reach the code that acts on
/// it is the failure this repository has already had twice - so these assert the behaviour, not the
/// value in the file.
/// </summary>
public class RequestSettingsTests
{
    private static readonly Workspace Ws = new() { RootPath = "x", Manifest = new AppManifest { Name = "t" } };

    private static RequestExecutionContext Context => new(Ws, null, []);

    private static HttpRequestExecutor Executor(HttpMessageHandler handler, AppSettings? settings = null) =>
        new(new SingleClientProvider(new HttpClient(handler)),
            new PassthroughResolver(),
            settings is null ? null : new FixedSettings(settings));

    private static AppSettings With(RequestSettings requests) => new() { Requests = requests };

    [Fact]
    public async Task The_timeout_setting_applies_to_a_request_that_names_none()
    {
        var request = new RequestModel { Name = "r", Method = "GET", Url = "https://slow.example/" };
        var settings = With(new RequestSettings { DefaultTimeoutSeconds = 1 });

        var result = await Executor(new NeverRespondsHandler(), settings).ExecuteAsync(request, Context);

        Assert.Contains("timed out", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal(0, result.StatusCode); // it never got one
    }

    [Fact]
    public async Task A_request_that_names_its_own_timeout_still_wins()
    {
        // The precedence that matters: a request saying "this one takes three minutes" means it, and a
        // global default of one second must not quietly override it into failing.
        var request = new RequestModel
        {
            Name = "r",
            Method = "GET",
            Url = "https://slow.example/",
            TimeoutSeconds = 30,
        };
        var settings = With(new RequestSettings { DefaultTimeoutSeconds = 1 });
        var handler = new DelayedHandler(TimeSpan.FromMilliseconds(200));

        var result = await Executor(handler, settings).ExecuteAsync(request, Context);

        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task The_size_setting_caps_the_body_that_is_read()
    {
        // 1 MB against a 2 MB response. Truncation keeps the status, headers and timing and reports
        // that the body was not loaded - it is not an error, and the request is not retried.
        var request = new RequestModel { Name = "r", Method = "GET", Url = "https://big.example/" };
        var settings = With(new RequestSettings { MaxResponseMegabytes = 1 });
        var handler = new FixedBodyHandler(new string('x', 2 * 1024 * 1024));

        var result = await Executor(handler, settings).ExecuteAsync(request, Context);

        Assert.Equal(200, result.StatusCode);
        Assert.True(result.BodyTooLarge);
        Assert.Equal("", result.Body);
    }

    [Fact]
    public async Task A_response_under_the_cap_is_read_whole()
    {
        var request = new RequestModel { Name = "r", Method = "GET", Url = "https://big.example/" };
        var settings = With(new RequestSettings { MaxResponseMegabytes = 1 });
        var handler = new FixedBodyHandler(new string('x', 1024));

        var result = await Executor(handler, settings).ExecuteAsync(request, Context);

        Assert.False(result.BodyTooLarge);
        Assert.Equal(1024, result.Body!.Length);
    }

    [Fact]
    public async Task With_no_settings_at_all_the_built_in_defaults_apply()
    {
        // The many tests - and the CLI - that construct the executor without settings.
        var request = new RequestModel { Name = "r", Method = "GET", Url = "https://big.example/" };

        var result = await Executor(new FixedBodyHandler("ok")).ExecuteAsync(request, Context);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("ok", result.Body);
    }

    private sealed class FixedBodyHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private sealed class DelayedHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }

    private sealed class NeverRespondsHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }
    }

    private sealed class FixedSettings(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Load() => settings;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SingleClientProvider(HttpClient client) : IScopedHttpClientProvider
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
