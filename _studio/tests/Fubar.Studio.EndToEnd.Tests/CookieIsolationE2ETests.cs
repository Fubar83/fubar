using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.EndToEnd.Tests;

/// <summary>Live end-to-end proof that session cookies are isolated per environment: a cookie set while
/// DEV is active is never replayed when the same requests run against PROD. Opt-in via FUBAR_E2E=1.</summary>
public class CookieIsolationE2ETests
{
    [Fact]
    public async Task A_cookie_set_in_one_environment_is_not_sent_in_another()
    {
        HttpBin.RequireLive();

        // One pipeline instance so the scoped cookie jars persist across the requests below.
        var (exec, ws) = HttpBin.Pipeline();
        var dev = new WorkspaceEnvironment { Name = "Dev" };
        var prod = new WorkspaceEnvironment { Name = "Prod" };

        async Task<string> Send(string url, WorkspaceEnvironment env)
        {
            var run = await exec.RunAsync(new RequestRun(HttpBin.Get(url), ws, env, EffectiveAuth: null, RecordHistory: false));
            return run.Result.Body;
        }

        // Set a session cookie while DEV is active (httpbin redirects to /cookies, which lists the jar).
        await Send($"{HttpBin.BaseUrl}/cookies/set?sessionid=DEV-COOKIE", dev);

        var devCookies = await Send($"{HttpBin.BaseUrl}/cookies", dev);
        var prodCookies = await Send($"{HttpBin.BaseUrl}/cookies", prod);

        Assert.Contains("DEV-COOKIE", devCookies);        // DEV's own jar kept it
        Assert.DoesNotContain("DEV-COOKIE", prodCookies); // PROD never saw it - jars are per environment
    }
}
