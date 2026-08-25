using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.EndToEnd.Tests;

/// <summary>
/// Shared harness for the live <c>httpbin.org</c> end-to-end auth tests. They exercise the REAL send
/// pipeline (auth prestep → HTTP executor → captures) to prove that auth actually reaches the wire - the
/// redesign's headline - and that cookies are isolated per environment. They double as runnable examples
/// of how each auth mode behaves.
/// <para>Opt-in (they need network and hit a public service): they self-skip unless <c>FUBAR_E2E=1</c>,
/// so CI stays offline and deterministic. Run locally with:
/// <c>FUBAR_E2E=1 dotnet test tests/Fubar.Studio.EndToEnd.Tests</c>.</para>
/// </summary>
public static class HttpBin
{
    /// <summary>The httpbin-compatible base URL. Defaults to the public service (or <c>FUBAR_E2E_BASEURL</c>);
    /// <see cref="HttpBinFixture"/> repoints it at auto-started local containers when a runtime is available.</summary>
    public static string BaseUrl { get; private set; } =
        (Environment.GetEnvironmentVariable("FUBAR_E2E_BASEURL") ?? "https://httpbin.org").TrimEnd('/');

    /// <summary>A second, DIFFERENT-origin echo endpoint - used to prove credentials are not forwarded
    /// across a real cross-host redirect.</summary>
    public static string OtherHostEcho { get; private set; } =
        Environment.GetEnvironmentVariable("FUBAR_E2E_OTHERHOST") ?? "https://postman-echo.com/get";

    /// <summary>Repoints the tests at a specific server pair (used by <see cref="HttpBinFixture"/> after it
    /// starts local containers).</summary>
    internal static void UseServer(string baseUrl, string otherHostEcho)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        OtherHostEcho = otherHostEcho;
    }

    /// <summary>Skips the calling test unless live e2e is explicitly enabled via <c>FUBAR_E2E=1</c>.</summary>
    public static void RequireLive() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("FUBAR_E2E") == "1",
            "Live httpbin e2e tests are opt-in. Set FUBAR_E2E=1 to run them.");

    /// <summary>A real, DI-wired execution pipeline (no OS keyring, no history writes). Reuse a single
    /// instance across requests that must share cookie jars (see the cookie-isolation test).</summary>
    public static (IRequestExecutionService Exec, Workspace Workspace) Pipeline()
    {
        var services = new ServiceCollection();
        services.AddFubarInfrastructure();
        services.AddSingleton<IRequestExecutionService, RequestExecutionService>();
        // Last registration wins - keep the OS keyring out of tests.
        services.AddSingleton<ISecretStoreService, NoSecretStore>();
        var provider = services.BuildServiceProvider();

        var workspace = new Workspace
        {
            RootPath = Path.Combine(Path.GetTempPath(), "fubar-e2e"),
            Manifest = new AppManifest { Id = "e2e", Name = "E2E" },
        };
        return (provider.GetRequiredService<IRequestExecutionService>(), workspace);
    }

    public static RequestModel Get(string url) => new() { Name = "e2e", Method = "GET", Url = url };

    /// <summary>Runs a request through the real pipeline (fresh isolated instance) with the given effective
    /// auth and optional active environment, returning the raw <see cref="ExecutionResult"/>.</summary>
    public static async Task<ExecutionResult> Send(AuthConfig? auth, RequestModel request, WorkspaceEnvironment? environment = null)
    {
        var (exec, workspace) = Pipeline();
        var run = await exec.RunAsync(new RequestRun(request, workspace, environment, auth, RecordHistory: false));
        return run.Result;
    }

    private sealed class NoSecretStore : ISecretStoreService
    {
        public string? TryGetSecret(string workspaceId, string key) => null;
        public void SetSecret(string workspaceId, string key, string value) { }
        public void DeleteSecret(string workspaceId, string key) { }
    }
}
