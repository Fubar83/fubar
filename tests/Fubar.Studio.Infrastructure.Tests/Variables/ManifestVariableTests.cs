using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Infrastructure.Variables;

namespace Fubar.Studio.Infrastructure.Tests.Variables;

/// <summary>
/// fubar.json's variables array was documented as "public workspace variables … committed to Git"
/// from the start, and VariableResolver never read it - built and never wired, which CLAUDE.md's
/// closing section names as this repository's recurring failure.
///
/// <para>It now resolves at the LOWEST precedence, which is the shape that makes it useful rather
/// than merely present: a workspace is runnable before anyone picks an environment, and an
/// environment can always override.</para>
/// </summary>
public class ManifestVariableTests
{
    private static Workspace WorkspaceWith(params AppVariable[] variables) => new()
    {
        RootPath = "/w",
        Manifest = new AppManifest { Id = "ws1", Name = "t", Variables = [.. variables] },
    };

    private static VariableResolver Resolver() =>
        new(new NoSecrets(), new SessionVariableStore());

    [Fact]
    public void A_manifest_variable_resolves_with_no_environment_at_all()
    {
        var workspace = WorkspaceWith(new AppVariable { Key = "baseUrl", Value = "https://api.example.com" });

        var resolution = Resolver().Resolve("baseUrl", workspace, activeEnvironment: null);

        Assert.True(resolution.IsDefined);
        Assert.Equal("https://api.example.com", resolution.Value);
        Assert.Equal("workspace", resolution.SourceName);
    }

    /// <summary>Bottom of the chain: an environment must always be able to override a workspace
    /// default, or picking an environment would stop meaning anything.</summary>
    [Fact]
    public void An_environment_beats_the_manifest()
    {
        var workspace = WorkspaceWith(new AppVariable { Key = "baseUrl", Value = "https://api.example.com" });
        var environment = new WorkspaceEnvironment
        {
            Name = "Staging",
            Variables = [new AppVariable { Key = "baseUrl", Value = "https://staging.example.com" }],
        };

        Assert.Equal("https://staging.example.com", Resolver().Resolve("baseUrl", workspace, environment).Value);
    }

    /// <summary>
    /// A declared-but-empty environment entry must not shadow the workspace default with "". Otherwise
    /// adding the key to one environment silently blanks it there, which reads as the manifest value
    /// being broken.
    /// </summary>
    [Fact]
    public void An_empty_environment_entry_falls_through_to_the_manifest()
    {
        var workspace = WorkspaceWith(new AppVariable { Key = "baseUrl", Value = "https://api.example.com" });
        var environment = new WorkspaceEnvironment
        {
            Name = "Staging",
            Variables = [new AppVariable { Key = "baseUrl", Value = "" }],
        };

        Assert.Equal("https://api.example.com", Resolver().Resolve("baseUrl", workspace, environment).Value);
    }

    /// <summary>
    /// fubar.json is committed, so a secret has no business in it. Refused rather than quietly read:
    /// the user marked it Secret expecting protection, and resolving it from a tracked file is the
    /// opposite of what they asked for.
    /// </summary>
    [Theory]
    [InlineData(VariableKind.Secret)]
    [InlineData(VariableKind.Session)]
    public void A_non_normal_manifest_variable_is_refused(VariableKind kind)
    {
        var workspace = WorkspaceWith(new AppVariable { Key = "token", Value = "should-not-be-here", Kind = kind });

        var resolution = Resolver().Resolve("token", workspace, activeEnvironment: null);

        Assert.False(resolution.IsDefined);
        Assert.Contains("environment", resolution.SourceName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Substitution_uses_the_manifest_too()
    {
        var workspace = WorkspaceWith(new AppVariable { Key = "version", Value = "v2" });

        Assert.Equal(
            "https://api.example.com/v2/orders",
            Resolver().Substitute("https://api.example.com/{{version}}/orders", workspace, activeEnvironment: null));
    }

    [Fact]
    public void Manifest_variables_are_offered_by_autocomplete()
    {
        var workspace = WorkspaceWith(
            new AppVariable { Key = "baseUrl", Value = "x" },
            new AppVariable { Key = "hidden", Value = "y", Kind = VariableKind.Secret });

        var available = Resolver().ListAvailable(workspace, activeEnvironment: null);

        Assert.Contains(available, v => v.Key == "baseUrl" && v.Source == "workspace");
        // Not offered, for the same reason it is not resolved.
        Assert.DoesNotContain(available, v => v.Key == "hidden");
    }

    private sealed class NoSecrets : ISecretStoreService
    {
        public string? TryGetSecret(string workspaceId, string key) => null;

        public void SetSecret(string workspaceId, string key, string value) { }

        public void DeleteSecret(string workspaceId, string key) { }
    }
}
