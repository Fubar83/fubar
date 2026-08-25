using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Fubar.Studio.EndToEnd.Tests;

/// <summary>
/// Manages the httpbin server the live auth e2e tests run against. When <c>FUBAR_E2E=1</c> (and no explicit
/// <c>FUBAR_E2E_BASEURL</c>), it auto-starts two throwaway httpbin containers via podman or docker - the
/// second on a different port, i.e. a different ORIGIN, for the cross-origin redirect tests - and tears them
/// down afterwards. If no container runtime is found (or startup fails) it falls back to the public services,
/// so the suite still runs. A third-party image is used deliberately - we don't hand-write a server to test
/// our own client.
/// </summary>
public sealed class HttpBinFixture : IAsyncLifetime
{
    private const string Image = "kennethreitz/httpbin";

    private string? _runtime;
    private readonly List<string> _containers = [];

    public async ValueTask InitializeAsync()
    {
        // Only manage anything when the suite is actually enabled and not pinned to a specific server.
        if (Environment.GetEnvironmentVariable("FUBAR_E2E") != "1"
            || Environment.GetEnvironmentVariable("FUBAR_E2E_BASEURL") is { Length: > 0 })
        {
            return;
        }

        _runtime = FindRuntime();
        if (_runtime is null)
        {
            Console.WriteLine("[e2e] no podman/docker found - using the public httpbin services.");
            return;
        }

        try
        {
            var primary = await StartContainerAsync();
            var other = await StartContainerAsync();
            HttpBin.UseServer($"http://localhost:{primary}", $"http://localhost:{other}/get");
            Console.WriteLine($"[e2e] using local {_runtime} httpbin containers on :{primary} and :{other}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[e2e] container startup failed ({ex.Message}); falling back to the public services.");
            await StopContainersAsync();
        }
    }

    public async ValueTask DisposeAsync() => await StopContainersAsync();

    private async Task<int> StartContainerAsync()
    {
        var port = FreePort();
        var name = $"fubar-e2e-httpbin-{Guid.NewGuid():n}";
        var (exit, output) = await RunAsync(_runtime!, $"run -d --rm -p {port}:80 --name {name} {Image}");
        if (exit != 0)
        {
            throw new InvalidOperationException($"'{_runtime} run' failed: {output.Trim()}");
        }

        _containers.Add(name);
        await WaitReadyAsync($"http://localhost:{port}/get");
        return port;
    }

    private async Task StopContainersAsync()
    {
        foreach (var name in _containers)
        {
            try
            {
                await RunAsync(_runtime!, $"rm -f {name}", timeoutMs: 30_000);
            }
            catch
            {
                // Best-effort teardown; --rm also removes the container when it stops.
            }
        }

        _containers.Clear();
    }

    private static string? FindRuntime()
    {
        foreach (var candidate in (string[])["podman", "docker"])
        {
            try
            {
                var (exit, _) = RunAsync(candidate, "--version", timeoutMs: 10_000).GetAwaiter().GetResult();
                if (exit == 0)
                {
                    return candidate;
                }
            }
            catch
            {
                // not installed / not on PATH
            }
        }

        return null;
    }

    private static async Task WaitReadyAsync(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if ((int)(await http.GetAsync(url)).StatusCode == 200)
                {
                    return;
                }
            }
            catch
            {
                // container not accepting connections yet
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"httpbin at {url} did not become ready in time.");
    }

    private static async Task<(int Exit, string Output)> RunAsync(string file, string arguments, int timeoutMs = 180_000)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"could not start '{file}'.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"'{file} {arguments}' timed out.");
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>Binds the httpbin fixture to every live e2e test class so the containers are started once for
/// the whole suite and torn down at the end.</summary>
[CollectionDefinition(Name)]
public sealed class HttpBinCollection : ICollectionFixture<HttpBinFixture>
{
    public const string Name = "HttpBin";
}
