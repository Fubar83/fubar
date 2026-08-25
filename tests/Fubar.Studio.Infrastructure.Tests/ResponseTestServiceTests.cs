using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
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

    private static ResponseTestService NewService() => new(new SessionVariableStore(), new RecordingWorkspaceService());

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
        var sut = new ResponseTestService(sessionStore, new RecordingWorkspaceService());
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
        var sut = new ResponseTestService(new SessionVariableStore(), recorder);
        var workspace = new Workspace { RootPath = "x", Manifest = new AppManifest { Name = "t" } };
        var environment = new WorkspaceEnvironment { Name = "Staging" };
        var capture = new CaptureRule { VariableName = "authToken", Source = ResponseField.JsonBody, Expression = "$.name", Scope = CaptureScope.Environment };

        var results = await sut.ApplyCapturesAsync([capture], SampleResult(), workspace, environment);

        Assert.True(Assert.Single(results).Ok);
        Assert.Contains(environment.Variables, v => v.Key == "authToken" && v.Value == "Ada");
        Assert.Single(recorder.SavedEnvironments);
    }
}
