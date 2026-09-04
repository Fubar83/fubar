using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Infrastructure.Variables;

namespace Fubar.Studio.Infrastructure.Tests.Variables;

/// <summary>
/// The source that makes a pipeline able to authenticate at all.
///
/// <para>A build agent has no OS keyring, so a Secret variable resolved to nothing, Substitute left the
/// token as literal text, and the run sent <c>Authorization: Bearer {{api_key}}</c> to a real endpoint
/// and reported a plain 401. The only workaround was to mark the variable Normal and commit its value -
/// so the missing feature was also what generated the insecure workaround.</para>
/// </summary>
public class ExternalVariableSourceTests
{
    private static Workspace Ws => new() { RootPath = "/w", Manifest = new AppManifest { Id = "ws1", Name = "t" } };

    [Fact]
    public void A_var_flag_supplies_a_value()
    {
        var source = ExternalVariableSource.Build(varFlags: ["api_key=abc123"]);

        Assert.Equal("abc123", source.TryGet("api_key"));
    }

    /// <summary>
    /// <c>{{api_key}}</c> has to be feedable by <c>FUBAR_VAR_API_KEY</c>: some CI systems allow no
    /// other shape for an environment variable name.
    /// </summary>
    [Theory]
    [InlineData("FUBAR_VAR_API_KEY", "api_key")]
    [InlineData("FUBAR_VAR_apiKey", "apiKey")]
    [InlineData("fubar_var_TOKEN", "token")]
    public void An_environment_variable_matches_its_workspace_name(string environmentName, string lookup)
    {
        var source = ExternalVariableSource.Build(
            environment: new Dictionary<string, string> { [environmentName] = "v" });

        Assert.Equal("v", source.TryGet(lookup));
    }

    [Fact]
    public void Variables_without_the_prefix_are_ignored()
    {
        var source = ExternalVariableSource.Build(
            environment: new Dictionary<string, string> { ["PATH"] = "/usr/bin", ["HOME"] = "/root" });

        Assert.Empty(source.Keys);
    }

    /// <summary>A flag beats a file beats the ambient environment - the order someone debugging a
    /// pipeline expects when they add a flag to override what the agent already sets.</summary>
    [Fact]
    public void A_flag_beats_a_file_beats_the_environment()
    {
        var source = ExternalVariableSource.Build(
            environment: new Dictionary<string, string> { ["FUBAR_VAR_HOST"] = "from-env", ["FUBAR_VAR_ONLY_ENV"] = "e" },
            envFileLines: ["host=from-file", "only_file=f"],
            varFlags: ["host=from-flag"]);

        Assert.Equal("from-flag", source.TryGet("host"));
        Assert.Equal("f", source.TryGet("only_file"));
        Assert.Equal("e", source.TryGet("only_env"));
    }

    /// <summary>A base64 secret ends in "=", so only the FIRST one separates key from value.</summary>
    [Fact]
    public void A_value_may_contain_equals_signs()
    {
        var source = ExternalVariableSource.Build(varFlags: ["secret=YWJjZGVmZw=="]);

        Assert.Equal("YWJjZGVmZw==", source.TryGet("secret"));
    }

    [Theory]
    [InlineData("# a comment")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-equals-sign")]
    public void Lines_that_carry_no_assignment_are_skipped(string line)
    {
        Assert.Empty(ExternalVariableSource.Build(envFileLines: [line]).Keys);
    }

    /// <summary>People paste these straight out of a shell script.</summary>
    [Fact]
    public void Export_and_quotes_are_tolerated()
    {
        var source = ExternalVariableSource.Build(envFileLines: ["export token=\"a b c\"", "other='x'"]);

        Assert.Equal("a b c", source.TryGet("token"));
        Assert.Equal("x", source.TryGet("other"));
    }

    /// <summary>
    /// The precedence that matters. It must beat the keyring, because on a build agent there is none -
    /// and it must beat the environment FILE, or a committed placeholder would shadow the real value
    /// the pipeline just supplied.
    /// </summary>
    [Fact]
    public void An_external_value_outranks_the_keyring_and_the_environment_file()
    {
        var external = ExternalVariableSource.Build(varFlags: ["api_key=from-ci"]);
        var resolver = new VariableResolver(new StubSecrets("from-keyring"), new SessionVariableStore(), external);
        var environment = new WorkspaceEnvironment
        {
            Name = "CI",
            Variables =
            [
                new AppVariable { Key = "api_key", Kind = VariableKind.Secret },
                new AppVariable { Key = "host", Value = "from-file" },
            ],
        };

        Assert.Equal("from-ci", resolver.Resolve("api_key", Ws, environment).Value);
        Assert.Equal("from-file", resolver.Resolve("host", Ws, environment).Value);
    }

    /// <summary>Without one, the resolver behaves exactly as it did - the GUI's case.</summary>
    [Fact]
    public void With_no_external_source_the_keyring_still_answers()
    {
        var resolver = new VariableResolver(new StubSecrets("from-keyring"), new SessionVariableStore());
        var environment = new WorkspaceEnvironment
        {
            Name = "Dev",
            Variables = [new AppVariable { Key = "api_key", Kind = VariableKind.Secret }],
        };

        Assert.Equal("from-keyring", resolver.Resolve("api_key", Ws, environment).Value);
    }

    private sealed class StubSecrets(string value) : ISecretStoreService
    {
        public string? TryGetSecret(string workspaceId, string key) => value;

        public void SetSecret(string workspaceId, string key, string secret) { }

        public void DeleteSecret(string workspaceId, string key) { }
    }
}
