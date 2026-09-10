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

        // curl already sends application/x-www-form-urlencoded for these, so saying it again is noise.
        Assert.DoesNotContain("Content-Type", curl, StringComparison.Ordinal);
    }

    // ---- The copied command has to BE the request ------------------------------------------------

    private static RequestModel Body(BodyType type, string raw, params KeyValueItem[] headers) => new()
    {
        Name = "r",
        Method = "POST",
        Url = "https://x.test/orders",
        Headers = [.. headers],
        Body = new RequestBody { Type = type, Raw = raw },
    };

    [Fact]
    public void A_json_body_states_its_content_type()
    {
        // `--data` makes curl send application/x-www-form-urlencoded. Without this the copied command
        // was a DIFFERENT request from the one the app sends - so "it works in curl" and "it fails in
        // the app" were comparing two things, which is the worst thing this button could be for.
        var curl = _sut.ToCurl(Body(BodyType.Json, "{\"name\":\"Ada\"}"), Resolve);

        Assert.Contains("-H 'Content-Type: application/json'", curl, StringComparison.Ordinal);
    }

    [Fact]
    public void Raw_text_and_a_binary_file_state_theirs_too()
    {
        Assert.Contains(
            "-H 'Content-Type: text/plain'",
            _sut.ToCurl(Body(BodyType.RawText, "hello"), Resolve),
            StringComparison.Ordinal);

        var binary = new RequestModel
        {
            Name = "r",
            Method = "POST",
            Url = "https://x.test/blob",
            Body = new RequestBody { Type = BodyType.BinaryFile, BinaryFilePath = "/tmp/x.bin" },
        };

        Assert.Contains(
            "-H 'Content-Type: application/octet-stream'",
            _sut.ToCurl(binary, Resolve),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_stated_content_type_is_not_doubled()
    {
        // The whole point of typing one is that it is the one that goes out - here and on the wire.
        var curl = _sut.ToCurl(
            Body(
                BodyType.Json,
                "{}",
                new KeyValueItem { Key = "Content-Type", Value = "application/vnd.acme.order+json", Enabled = true }),
            Resolve);

        Assert.Contains("-H 'Content-Type: application/vnd.acme.order+json'", curl, StringComparison.Ordinal);
        Assert.DoesNotContain("application/json'", curl, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_body_states_nothing()
    {
        var curl = _sut.ToCurl(Body(BodyType.Json, ""), Resolve);

        Assert.DoesNotContain("Content-Type", curl, StringComparison.Ordinal);
    }
}
