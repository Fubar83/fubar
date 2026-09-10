using Fubar.Studio.Core.Import;
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

    // ---- The PowerShell form ---------------------------------------------------------------------
    //
    // The POSIX command does not merely look wrong in PowerShell, it fails:  is not a line
    // continuation there, so the parser stops at the first one with "Missing expression after unary
    // operator '--'" having sent nothing at all. Verified by running both forms through pwsh.

    private static RequestModel WithApostrophe => Body(
        BodyType.Json,
        """{"note":"Ada's order"}""",
        new KeyValueItem { Key = "Accept", Value = "application/vnd.acme.order+json;version=2", Enabled = true });

    [Fact]
    public void PowerShell_gets_one_line_because_a_backslash_is_not_a_continuation()
    {
        var curl = _sut.ToCurl(WithApostrophe, Resolve, CurlShell.PowerShell);

        Assert.DoesNotContain("\n", curl, StringComparison.Ordinal);
        Assert.DoesNotContain(@" \", curl, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShell_calls_curl_exe_so_the_5_1_alias_cannot_take_it()
    {
        // In Windows PowerShell 5.1 `curl` is an alias for Invoke-WebRequest, which understands none of
        // -X, -H or --data and fails on the first of them whatever the quoting.
        var curl = _sut.ToCurl(WithApostrophe, Resolve, CurlShell.PowerShell);

        Assert.StartsWith("curl.exe ", curl, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShell_doubles_an_apostrophe_rather_than_escaping_it_the_POSIX_way()
    {
        var powershell = _sut.ToCurl(WithApostrophe, Resolve, CurlShell.PowerShell);
        var posix = _sut.ToCurl(WithApostrophe, Resolve);

        Assert.Contains("""{"note":"Ada''s order"}""", powershell, StringComparison.Ordinal);
        Assert.DoesNotContain(@"'\''", powershell, StringComparison.Ordinal);

        // And the POSIX one keeps doing it the POSIX way: close the string, escape a quote, reopen.
        Assert.Contains(@"Ada'\''s order", posix, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_forms_carry_the_same_request()
    {
        // The shells differ in quoting and line breaks and in nothing else. A flag that reached one
        // form and not the other would send two different requests from one button.
        var request = Body(
            BodyType.Json,
            """{"sku":"A"}""",
            new KeyValueItem { Key = "Accept", Value = "application/json", Enabled = true });

        var powershell = _sut.ToCurl(request, Resolve, CurlShell.PowerShell);
        var posix = _sut.ToCurl(request, Resolve);

        foreach (var expected in new[] { "-X POST", "'https://x.test/orders'", "-H 'Accept: application/json'", "-H 'Content-Type: application/json'", "--data '{\"sku\":\"A\"}'" })
        {
            Assert.Contains(expected, powershell, StringComparison.Ordinal);
            Assert.Contains(expected, posix, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_default_is_still_POSIX()
    {
        // Every existing caller, and every curl command anyone has ever read.
        Assert.Equal(_sut.ToCurl(WithApostrophe, Resolve), _sut.ToCurl(WithApostrophe, Resolve, CurlShell.Posix));
        Assert.Contains(" \\\n", _sut.ToCurl(WithApostrophe, Resolve), StringComparison.Ordinal);
    }
}
