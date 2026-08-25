using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Models;

public class RequestModelDefaultsTests
{
    [Fact]
    public void NewRequest_DefaultsToHttpGet()
    {
        var request = new RequestModel { Name = "Untitled" };

        Assert.Equal(RequestKind.Http, request.Kind);
        Assert.Equal("GET", request.Method);
        Assert.Equal(BodyType.None, request.Body.Type);
        Assert.Equal(AuthType.Inherit, request.Auth.Type);
    }

    [Fact]
    public void NewRequest_GetsAUniqueId()
    {
        var first = new RequestModel { Name = "A" };
        var second = new RequestModel { Name = "B" };

        Assert.NotEqual(first.Id, second.Id);
    }
}
