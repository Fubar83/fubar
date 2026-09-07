using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Infrastructure.Testing;
using Fubar.Studio.Infrastructure.Variables;

namespace Fubar.Studio.Infrastructure.Tests;

public class ResponseTestServiceTests
{
    private static ExecutionResult SampleResult() => new()
    {
        StatusCode = 200,
        ReasonPhrase = "OK",
        Body = "{\"id\":42,\"name\":\"Ada\"}",
        Headers = [new KeyValueItem { Key = "Content-Type", Value = "application/json" }],
        ElapsedMilliseconds = 50,
        SizeBytes = 20,
    };

    private static ResponseTestService NewService() => NewService(new SessionVariableStore(), new RecordingWorkspaceService());

    private static ResponseTestService NewService(SessionVariableStore session, IEnvironmentStore environments) =>
        new(session, environments, new VariableWriter(new NoSecrets(), session));

    /// <summary>A keyring that holds nothing - these tests are about the environment and session paths.</summary>
    private sealed class NoSecrets : ISecretStoreService
    {
        public string? TryGetSecret(string workspaceId, string key) => null;

        public void SetSecret(string workspaceId, string key, string value) { }

        public void DeleteSecret(string workspaceId, string key) { }
    }

    [Theory]
    [InlineData(ResponseField.StatusCode, "", AssertionOperator.Equals, "200", true)]
    [InlineData(ResponseField.StatusCode, "", AssertionOperator.Equals, "404", false)]
    [InlineData(ResponseField.JsonBody, "$.name", AssertionOperator.Equals, "Ada", true)]
    [InlineData(ResponseField.JsonBody, "$.id", AssertionOperator.Equals, "42", true)]
    [InlineData(ResponseField.JsonBody, "$.missing", AssertionOperator.Exists, "", false)]
    [InlineData(ResponseField.JsonBody, "$.id", AssertionOperator.Exists, "", true)]
    [InlineData(ResponseField.Header, "Content-Type", AssertionOperator.Contains, "json", true)]
    [InlineData(ResponseField.Header, "X-Absent", AssertionOperator.NotExists, "", true)]
    [InlineData(ResponseField.ResponseTimeMs, "", AssertionOperator.LessThan, "1000", true)]
    [InlineData(ResponseField.ResponseTimeMs, "", AssertionOperator.GreaterThan, "1000", false)]
    public void Assertions_evaluate_expected_pass_fail(ResponseField source, string target, AssertionOperator op, string expected, bool shouldPass)
    {
        var assertion = new Assertion { Source = source, Target = target, Operator = op, Expected = expected };

        var result = Assert.Single(NewService().RunAssertions([assertion], SampleResult()));

        Assert.Equal(shouldPass, result.Passed);
    }

    [Fact]
    public void Disabled_assertions_are_skipped()
    {
        var assertion = new Assertion { Enabled = false, Source = ResponseField.StatusCode, Operator = AssertionOperator.Equals, Expected = "200" };

        Assert.Empty(NewService().RunAssertions([assertion], SampleResult()));
    }

    [Fact]
    public async Task Session_capture_writes_to_the_session_store()
    {
        var sessionStore = new SessionVariableStore();
        var sut = NewService(sessionStore, new RecordingWorkspaceService());
        var workspace = new Workspace { RootPath = "x", Manifest = new AppManifest { Name = "t" } };
        var capture = new CaptureRule { VariableName = "userId", Source = ResponseField.JsonBody, Expression = "$.id", Scope = CaptureScope.Session };

        var results = await sut.ApplyCapturesAsync([capture], SampleResult(), workspace, activeEnvironment: null);

        Assert.True(Assert.Single(results).Ok);
        // Session captures are written under the per-(workspace,environment) scope.
        Assert.True(sessionStore.TryGet(SessionScope.For(workspace, (WorkspaceEnvironment?)null), "userId", out var value));
        Assert.Equal("42", value);
    }

    [Fact]
    public async Task Environment_capture_updates_and_persists_the_environment()
    {
        var recorder = new RecordingWorkspaceService();
        var sut = NewService(new SessionVariableStore(), recorder);
        var workspace = new Workspace { RootPath = "x", Manifest = new AppManifest { Name = "t" } };
        var environment = new WorkspaceEnvironment { Name = "Staging" };
        var capture = new CaptureRule { VariableName = "authToken", Source = ResponseField.JsonBody, Expression = "$.name", Scope = CaptureScope.Environment };

        var results = await sut.ApplyCapturesAsync([capture], SampleResult(), workspace, environment);

        Assert.True(Assert.Single(results).Ok);
        Assert.Contains(environment.Variables, v => v.Key == "authToken" && v.Value == "Ada");
        Assert.Single(recorder.SavedEnvironments);
    }

    /// <summary>
    /// The leak this whole change exists for. Capturing into a variable the user marked Secret used to
    /// assign <c>AppVariable.Value</c> directly and persist it - so the token landed in
    /// <c>environments/*.json</c>, which the product tells you to commit, and the null that
    /// <c>AppVariable</c> documents as always being on disk for a secret was overwritten.
    /// </summary>
    [Fact]
    public async Task Capture_into_a_secret_variable_never_writes_its_value_to_disk()
    {
        var secrets = new RecordingSecretStore();
        var recorder = new RecordingWorkspaceService();
        var sut = new ResponseTestService(new SessionVariableStore(), recorder, new VariableWriter(secrets, new SessionVariableStore()));
        var workspace = new Workspace { RootPath = "x", Manifest = new AppManifest { Id = "ws1", Name = "t" } };
        var environment = new WorkspaceEnvironment
        {
            Name = "Staging",
            Variables = [new AppVariable { Key = "authToken", Kind = VariableKind.Secret }],
        };
        var capture = new CaptureRule { VariableName = "authToken", Source = ResponseField.JsonBody, Expression = "$.name", Scope = CaptureScope.Environment };

        await sut.ApplyCapturesAsync([capture], SampleResult(), workspace, environment);

        // On disk: still declared, still valueless.
        Assert.Null(Assert.Single(environment.Variables).Value);
        // In the keyring: the real value.
        Assert.Equal("Ada", secrets.Get("ws1", "authToken"));
    }

    /// <summary>
    /// A Normal variable whose NAME looks like a credential still gets captured - the user may mean it -
    /// but the result carries a warning, because the value really is going into a tracked file.
    /// </summary>
    [Fact]
    public async Task An_environment_capture_of_a_credential_shaped_name_warns()
    {
        var sut = NewService(new SessionVariableStore(), new RecordingWorkspaceService());
        var workspace = new Workspace { RootPath = "x", Manifest = new AppManifest { Name = "t" } };
        var environment = new WorkspaceEnvironment { Name = "Staging" };
        var capture = new CaptureRule { VariableName = "access_token", Source = ResponseField.JsonBody, Expression = "$.name", Scope = CaptureScope.Environment };

        var result = Assert.Single(await sut.ApplyCapturesAsync([capture], SampleResult(), workspace, environment));

        Assert.True(result.Ok);
        Assert.NotNull(result.Warning);
        Assert.Contains("Session scope", result.Warning);
    }

    /// <summary>A session capture goes nowhere near a file, so it has nothing to warn about.</summary>
    [Fact]
    public async Task A_session_capture_of_a_credential_shaped_name_does_not_warn()
    {
        var sut = NewService(new SessionVariableStore(), new RecordingWorkspaceService());
        var workspace = new Workspace { RootPath = "x", Manifest = new AppManifest { Name = "t" } };
        var capture = new CaptureRule { VariableName = "access_token", Source = ResponseField.JsonBody, Expression = "$.name", Scope = CaptureScope.Session };

        var result = Assert.Single(await sut.ApplyCapturesAsync([capture], SampleResult(), workspace, activeEnvironment: null));

        Assert.True(result.Ok);
        Assert.Null(result.Warning);
    }

    /// <summary>A keyring that remembers, so a test can assert the value went there instead of to disk.</summary>
    private sealed class RecordingSecretStore : ISecretStoreService
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Get(string workspaceId, string key) => TryGetSecret(workspaceId, key);

        public string? TryGetSecret(string workspaceId, string key) =>
            _values.TryGetValue($"{workspaceId}:{key}", out var value) ? value : null;

        public void SetSecret(string workspaceId, string key, string value) => _values[$"{workspaceId}:{key}"] = value;

        public void DeleteSecret(string workspaceId, string key) => _values.Remove($"{workspaceId}:{key}");
    }
}
