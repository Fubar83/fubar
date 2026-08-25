using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Core.Tests.Auth;

public class EffectiveAuthResolverTests
{
    private static readonly AuthProfile Prod = new() { Id = "p1", Name = "Prod", Config = new AuthConfig { Type = AuthType.Bearer, Token = "prod" } };

    private static EffectiveAuth Resolve(AuthType type, AuthConfig? inline = null, AuthProfile? selected = null, InheritanceChain? chain = null) =>
        EffectiveAuthResolver.Resolve(type, inline ?? new AuthConfig(), selected, chain, [Prod]);

    [Fact]
    public void Selected_profile_wins()
    {
        var result = Resolve(AuthType.Profile, selected: Prod);
        Assert.Same(Prod.Config, result.Config);
        Assert.Equal("Auth: Prod", result.Source);
    }

    [Fact]
    public void Inline_scheme_uses_this_requests_config()
    {
        var inline = new AuthConfig { Type = AuthType.Bearer, Token = "inline" };
        var result = Resolve(AuthType.Bearer, inline);
        Assert.Same(inline, result.Config);
        Assert.Equal("Auth (this request)", result.Source);
    }

    [Fact]
    public void Inherit_resolves_through_the_folder_chain()
    {
        var chain = new InheritanceChain([], Prod.Id, "Folder: api");
        var result = Resolve(AuthType.Inherit, chain: chain);
        Assert.Same(Prod.Config, result.Config);
        Assert.Equal("Folder: api", result.Source);
    }

    [Fact]
    public void Inherit_without_a_matching_profile_yields_nothing()
    {
        var chain = new InheritanceChain([], "unknown", "Folder: api");
        var result = Resolve(AuthType.Inherit, chain: chain);
        Assert.Null(result.Config);
    }

    [Fact]
    public void None_yields_nothing()
    {
        Assert.Null(Resolve(AuthType.None).Config);
    }
}
