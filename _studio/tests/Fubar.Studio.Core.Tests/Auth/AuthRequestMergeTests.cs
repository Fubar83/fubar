using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Auth;

public class AuthRequestMergeTests
{
    private static RequestModel Request(params KeyValueItem[] headers) =>
        new() { Name = "r", Headers = [.. headers] };

    [Fact]
    public void Injects_auth_header_when_absent()
    {
        var applied = new AppliedAuth([new KeyValueItem { Key = "Authorization", Value = "Bearer tok" }], []);

        var result = AuthRequestMerge.Inject(Request(), applied);

        var header = Assert.Single(result.Headers);
        Assert.Equal("Authorization", header.Key);
        Assert.Equal("Bearer tok", header.Value);
    }

    [Fact]
    public void Does_not_overwrite_an_existing_enabled_header()
    {
        var request = Request(new KeyValueItem { Key = "Authorization", Value = "Bearer mine", Enabled = true });
        var applied = new AppliedAuth([new KeyValueItem { Key = "authorization", Value = "Bearer injected" }], []);

        var result = AuthRequestMerge.Inject(request, applied);

        Assert.Equal("Bearer mine", Assert.Single(result.Headers).Value); // user's explicit header wins
    }

    [Fact]
    public void Appends_auth_query_params()
    {
        var applied = new AppliedAuth([], [new KeyValueItem { Key = "api_key", Value = "k" }]);

        var result = AuthRequestMerge.Inject(Request(), applied);

        var param = Assert.Single(result.QueryParams);
        Assert.Equal("api_key", param.Key);
        Assert.Equal("k", param.Value);
    }

    [Fact]
    public void Empty_applied_returns_the_same_request()
    {
        var request = Request();

        Assert.Same(request, AuthRequestMerge.Inject(request, AppliedAuth.Empty));
    }
}
