using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Import;

namespace Fubar.Studio.Infrastructure.Tests;

public class CurlImporterTests
{
    private readonly CurlImporter _sut = new();

    [Fact]
    public void Parses_method_url_headers_query_and_json_body()
    {
        var request = _sut.Parse(
            "curl -X POST 'https://api.example.com/v1/users?active=true' " +
            "-H 'Authorization: Bearer abc' -H 'Content-Type: application/json' " +
            "-d '{\"name\":\"Ada\"}'");

        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.example.com/v1/users", request.Url);
        Assert.Contains(request.QueryParams, p => p.Key == "active" && p.Value == "true");
        Assert.Contains(request.Headers, h => h.Key == "Authorization" && h.Value == "Bearer abc");
        Assert.Equal(BodyType.Json, request.Body.Type);
        Assert.Contains("Ada", request.Body.Raw);
    }

    [Fact]
    public void Data_flag_implies_post_and_urlencoded_body()
    {
        var request = _sut.Parse("curl https://x.test/submit -d 'a=1&b=2'");

        Assert.Equal("POST", request.Method);
        Assert.Equal(BodyType.UrlEncoded, request.Body.Type);
        Assert.Contains(request.Body.UrlEncoded, p => p.Key == "a" && p.Value == "1");
        Assert.Contains(request.Body.UrlEncoded, p => p.Key == "b" && p.Value == "2");
    }

    [Fact]
    public void Basic_auth_flag_maps_to_basic_auth()
    {
        var request = _sut.Parse("curl -u alice:s3cret https://x.test");

        Assert.Equal("GET", request.Method);
        Assert.Equal(AuthType.Basic, request.Auth.Type);
        Assert.Equal("alice", request.Auth.Username);
        Assert.Equal("s3cret", request.Auth.Password);
    }

    [Fact]
    public void Bare_url_is_a_get_with_a_derived_name()
    {
        var request = _sut.Parse("curl https://x.test/path/here");

        Assert.Equal("GET", request.Method);
        Assert.Equal("https://x.test/path/here", request.Url);
        Assert.Equal("GET /path/here", request.Name);
    }

    [Fact]
    public void Line_continuations_are_honoured()
    {
        var request = _sut.Parse("curl https://x.test \\\n  -H 'X-A: 1' \\\n  -H 'X-B: 2'");

        Assert.Contains(request.Headers, h => h.Key == "X-A" && h.Value == "1");
        Assert.Contains(request.Headers, h => h.Key == "X-B" && h.Value == "2");
    }

    [Fact]
    public void Missing_url_throws()
    {
        Assert.Throws<FormatException>(() => _sut.Parse("curl -X POST -H 'A: b'"));
    }
}
