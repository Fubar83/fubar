using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Infrastructure.Settings;
using Fubar.Studio.Infrastructure.Testing;
using Fubar.Studio.Infrastructure.Variables;

namespace Fubar.Studio.Infrastructure.Tests.Settings;

/// <summary>
/// AppSettings is one per-user file, so nothing could be required of an installation - including the
/// one setting that leaks credentials, capturing to Environment scope.
/// </summary>
public class MachinePolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "fubar-policy-" + Guid.NewGuid().ToString("n"));

    public MachinePolicyTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string WritePolicy(string json)
    {
        var path = Path.Combine(_directory, "policy.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void No_file_means_no_policy()
    {
        var service = new MachinePolicyService(Path.Combine(_directory, "absent.json"));

        Assert.True(service.Current.IsEmpty);
        Assert.Null(service.Source);
    }

    [Fact]
    public void A_policy_file_is_read()
    {
        var service = new MachinePolicyService(WritePolicy("""{ "forbidEnvironmentCaptures": true }"""));

        Assert.True(service.Current.ForbidEnvironmentCaptures);
        Assert.NotNull(service.Source);
    }

    /// <summary>
    /// Refusing to launch over an administrator's typo is a worse failure than the one it would be
    /// reporting - and applying half a policy would be worse than either.
    /// </summary>
    [Fact]
    public void A_malformed_policy_applies_nothing_and_says_so()
    {
        var service = new MachinePolicyService(WritePolicy("{ not json"));

        Assert.True(service.Current.IsEmpty);
        Assert.Contains("could not be read", service.Source!, StringComparison.Ordinal);
    }

    /// <summary>The key that matters: Environment scope writes a captured token to a committed file.</summary>
    [Fact]
    public async Task Forbidding_environment_captures_refuses_one_and_names_the_policy()
    {
        var sut = new ResponseTestService(
            new SessionVariableStore(),
            new RecordingWorkspaceService(),
            new VariableWriter(new NoSecrets(), new SessionVariableStore()),
            new MachinePolicyService(WritePolicy("""{ "forbidEnvironmentCaptures": true }""")));

        var workspace = new Workspace { RootPath = "x", Manifest = new AppManifest { Name = "t" } };
        var environment = new WorkspaceEnvironment { Name = "Staging" };
        var capture = new CaptureRule
        {
            VariableName = "token",
            Source = ResponseField.JsonBody,
            Expression = "$.name",
            Scope = CaptureScope.Environment,
        };

        var result = Assert.Single(await sut.ApplyCapturesAsync([capture], SampleResult(), workspace, environment));

        Assert.False(result.Ok);
        Assert.Contains("policy", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A session capture is untouched: it never reaches a file, so there is nothing to forbid.</summary>
    [Fact]
    public async Task A_session_capture_is_unaffected_by_the_policy()
    {
        var session = new SessionVariableStore();
        var sut = new ResponseTestService(
            session,
            new RecordingWorkspaceService(),
            new VariableWriter(new NoSecrets(), session),
            new MachinePolicyService(WritePolicy("""{ "forbidEnvironmentCaptures": true }""")));

        var workspace = new Workspace { RootPath = "x", Manifest = new AppManifest { Name = "t" } };
        var capture = new CaptureRule
        {
            VariableName = "token",
            Source = ResponseField.JsonBody,
            Expression = "$.name",
            Scope = CaptureScope.Session,
        };

        Assert.True(Assert.Single(await sut.ApplyCapturesAsync([capture], SampleResult(), workspace, null)).Ok);
    }

    private static ExecutionResult SampleResult() => new()
    {
        StatusCode = 200,
        Body = "{\"id\":42,\"name\":\"Ada\"}",
    };

    private sealed class NoSecrets : ISecretStoreService
    {
        public string? TryGetSecret(string workspaceId, string key) => null;

        public void SetSecret(string workspaceId, string key, string value) { }

        public void DeleteSecret(string workspaceId, string key) { }
    }
}
