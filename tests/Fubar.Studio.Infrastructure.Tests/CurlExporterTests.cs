using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Import;

namespace Fubar.Studio.Infrastructure.Tests;

public class CurlExporterTests
{
    private readonly CurlExporter _sut = new();

    // Resolver that mimics {{var}} substitution for the tests.
    private static string Resolve(string? s) => (s ?? "").Replace("{{token}}", "abc123");

    [Fact]
    public void Emits_method_url_headers_and_json_body()
    {
        var request = new RequestModel
        {
            Name = "Create",
            Method = "POST",
            Url = "https://api.example.com/v1/users?active=true",
            Headers =
            [
                new KeyValueItem { Key = "Authorization", Value = "Bearer {{token}}", Enabled = true },
                new KeyValueItem { Key = "X-Off", Value = "no", Enabled = false },
            ],
            Body = new RequestBody { Type = BodyType.Json, Raw = "{\"name\":\"Ada\"}" },
        };

        var curl = _sut.ToCurl(request, Resolve);

        Assert.Contains("-X POST", curl);
        Assert.Contains("'https://api.example.com/v1/users?active=true'", curl);
        Assert.Contains("-H 'Authorization: Bearer abc123'", curl);
        Assert.DoesNotContain("X-Off", curl); // disabled header omitted
        Assert.Contains("--data '{\"name\":\"Ada\"}'", curl);
    }

    [Fact]
    public void Get_without_body_omits_method_flag()
    {
        var request = new RequestModel { Name = "List", Method = "GET", Url = "https://x.test/items" };

        var curl = _sut.ToCurl(request, Resolve);

        Assert.DoesNotContain("-X", curl);
        Assert.StartsWith("curl 'https://x.test/items'", curl);
    }

    [Fact]
    public void Single_quotes_in_values_are_escaped()
    {
        var request = new RequestModel
        {
            Name = "Q",
            Method = "GET",
            Url = "https://x.test/search?q=it's",
        };

        var curl = _sut.ToCurl(request, Resolve);

        Assert.Contains("'\\''", curl); // the ' in it's is shell-escaped
    }

    [Fact]
    public void Urlencoded_body_emits_data_urlencode_flags()
    {
        var request = new RequestModel
        {
            Name = "Form",
            Method = "POST",
            Url = "https://x.test/login",
            Body = new RequestBody
            {
                Type = BodyType.UrlEncoded,
                UrlEncoded = [new KeyValueItem { Key = "user", Value = "ada", Enabled = true }],
            },
        };

        var curl = _sut.ToCurl(request, Resolve);

        Assert.Contains("--data-urlencode 'user=ada'", curl);
    }
}
