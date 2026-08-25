using Fubar.Studio.Core.Http;

namespace Fubar.Studio.Core.Tests.Http;

public class QueryStringSyncTests
{
    [Fact]
    public void ParseQuery_decodes_pairs()
    {
        var pairs = QueryStringSync.ParseQuery("https://x.test/path?a=1&b=hello%20world");
        Assert.Equal(2, pairs.Count);
        Assert.Equal(("a", "1"), pairs[0]);
        Assert.Equal(("b", "hello world"), pairs[1]);
    }

    [Fact]
    public void ParseQuery_without_query_is_empty()
    {
        Assert.Empty(QueryStringSync.ParseQuery("https://x.test/path"));
    }

    [Fact]
    public void BasePart_strips_the_query()
    {
        Assert.Equal("https://x.test/path", QueryStringSync.BasePart("https://x.test/path?a=1"));
        Assert.Equal("https://x.test/path", QueryStringSync.BasePart("https://x.test/path"));
    }

    [Fact]
    public void BuildUrl_encodes_and_drops_blank_keys()
    {
        var url = QueryStringSync.BuildUrl("https://x.test/s?old=1", [("q", "a b"), ("", "skip"), ("n", "2")]);
        Assert.Equal("https://x.test/s?q=a%20b&n=2", url);
    }

    [Fact]
    public void BuildUrl_with_no_params_returns_base()
    {
        Assert.Equal("https://x.test/s", QueryStringSync.BuildUrl("https://x.test/s?old=1", []));
    }
}
