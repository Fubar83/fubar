using Fubar.Studio.Core.Models;

namespace Fubar.Studio.EndToEnd.Tests;

/// <summary>Live end-to-end verification of the cross-origin redirect hardening, across TWO real hosts:
/// httpbin.org issues a 302 to postman-echo.com (a different origin), which echoes the headers it received.
/// Injected credentials must NOT reach the other host; a non-credential header and same-origin redirects
/// must be unaffected. Opt-in via FUBAR_E2E=1.</summary>
public class AuthRedirectE2ETests
{
    // httpbin.org/redirect-to?url=<target>&status_code=302 → a real 302 to <target>.
    private static RequestModel RedirectingTo(string target, params KeyValueItem[] headers) => new()
    {
        Name = "e2e",
        Method = "GET",
        Url = $"{HttpBin.BaseUrl}/redirect-to",
        QueryParams =
        [
            new KeyValueItem { Key = "url", Value = target },
            new KeyValueItem { Key = "status_code", Value = "302" },
        ],
        Headers = [.. headers],
    };

    [Fact]
    public async Task ApiKey_header_is_not_forwarded_across_a_cross_origin_redirect()
    {
        HttpBin.RequireLive();

        var request = RedirectingTo(HttpBin.OtherHostEcho, new KeyValueItem { Key = "X-Trace", Value = "safe-trace-777" });
        var auth = new AuthConfig { Type = AuthType.ApiKey, ApiKeyName = "X-Api-Key", ApiKeyValue = "leaky-api-key-777", ApiKeyLocation = ApiKeyLocation.Header };

        var result = await HttpBin.Send(auth, request);

        Assert.Equal(200, result.StatusCode);                    // followed the 302 to postman-echo
        Assert.DoesNotContain("leaky-api-key-777", result.Body); // the API key did NOT reach the other host
        Assert.Contains("safe-trace-777", result.Body);          // a normal header still followed the redirect
    }

    [Fact]
    public async Task Bearer_token_is_not_forwarded_across_a_cross_origin_redirect()
    {
        HttpBin.RequireLive();

        var auth = new AuthConfig { Type = AuthType.Bearer, Token = "leaky-bearer-777" };

        var result = await HttpBin.Send(auth, RedirectingTo(HttpBin.OtherHostEcho));

        Assert.Equal(200, result.StatusCode);
        Assert.DoesNotContain("leaky-bearer-777", result.Body);  // Authorization stripped on the cross-origin hop
    }

    [Fact]
    public async Task Query_api_key_is_not_forwarded_across_a_redirect()
    {
        HttpBin.RequireLive();

        var auth = new AuthConfig { Type = AuthType.ApiKey, ApiKeyName = "api_key", ApiKeyValue = "leaky-query-777", ApiKeyLocation = ApiKeyLocation.QueryParam };

        var result = await HttpBin.Send(auth, RedirectingTo(HttpBin.OtherHostEcho));

        Assert.Equal(200, result.StatusCode);
        Assert.DoesNotContain("leaky-query-777", result.Body);   // query auth is dropped when following Location
    }

    [Fact]
    public async Task ApiKey_header_IS_kept_across_a_same_origin_redirect()
    {
        HttpBin.RequireLive();

        // Relative target -> httpbin.org/headers (same origin), which echoes the request headers.
        var request = RedirectingTo("/headers");
        var auth = new AuthConfig { Type = AuthType.ApiKey, ApiKeyName = "X-Api-Key", ApiKeyValue = "kept-key-777", ApiKeyLocation = ApiKeyLocation.Header };

        var result = await HttpBin.Send(auth, request);

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("kept-key-777", result.Body);            // same-origin redirect keeps the credential
    }
}
