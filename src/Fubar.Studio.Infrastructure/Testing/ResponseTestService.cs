using System.Globalization;
using System.Text.Json.Nodes;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;
using Json.Path;

namespace Fubar.Studio.Infrastructure.Testing;

/// <summary>
/// Evaluates a request's assertions and applies its capture rules against an <see cref="ExecutionResult"/>.
/// Value extraction (JSONPath / header / status / time) is shared between the two so they behave
/// identically. Captures with <see cref="CaptureScope.Session"/> go to the in-memory
/// <see cref="ISessionVariableStore"/>; <see cref="CaptureScope.Environment"/> updates the active
/// environment in place and persists it.
/// </summary>
public sealed class ResponseTestService : IResponseTestService
{
    private readonly ISessionVariableStore _sessionStore;
    private readonly IEnvironmentStore _workspaceService;

    public ResponseTestService(ISessionVariableStore sessionStore, IEnvironmentStore workspaceService)
    {
        _sessionStore = sessionStore;
        _workspaceService = workspaceService;
    }

    public IReadOnlyList<AssertionResult> RunAssertions(IReadOnlyList<Assertion> assertions, ExecutionResult result)
    {
        var body = new Lazy<JsonNode?>(() => ParseBody(result.Body));
        var results = new List<AssertionResult>();

        foreach (var a in assertions.Where(a => a.Enabled))
        {
            results.Add(Evaluate(a, result, body));
        }

        return results;
    }

    public async Task<IReadOnlyList<CaptureResult>> ApplyCapturesAsync(
        IReadOnlyList<CaptureRule> captures,
        ExecutionResult result,
        Workspace workspace,
        WorkspaceEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        var body = new Lazy<JsonNode?>(() => ParseBody(result.Body));
        var results = new List<CaptureResult>();
        var environmentDirty = false;

        foreach (var c in captures.Where(c => c.Enabled))
        {
            var name = c.VariableName.Trim();
            if (name.Length == 0)
            {
                results.Add(new CaptureResult(false, c.VariableName, null, c.Scope.ToString(), "No variable name."));
                continue;
            }

            var (found, value) = ReadValue(c.Source, c.Expression, result, body);
            if (!found)
            {
                results.Add(new CaptureResult(false, name, null, c.Scope.ToString(),
                    $"No value for {Describe(c.Source, c.Expression)}."));
                continue;
            }

            if (c.Scope == CaptureScope.Session)
            {
                _sessionStore.Set(SessionScope.For(workspace, activeEnvironment), name, value);
                results.Add(new CaptureResult(true, name, value, "session", null));
            }
            else if (activeEnvironment is null)
            {
                results.Add(new CaptureResult(false, name, value, "environment",
                    "No active environment to write to."));
            }
            else
            {
                SetEnvironmentVariable(activeEnvironment, name, value);
                environmentDirty = true;
                results.Add(new CaptureResult(true, name, value, activeEnvironment.Name, null));
            }
        }

        if (environmentDirty && activeEnvironment is not null)
        {
            await _workspaceService.SaveEnvironmentAsync(workspace.RootPath, activeEnvironment, cancellationToken);
        }

        return results;
    }

    private static void SetEnvironmentVariable(WorkspaceEnvironment environment, string name, string? value)
    {
        var existing = environment.Variables.FirstOrDefault(v => v.Key == name);
        if (existing is not null)
        {
            existing.Value = value ?? "";
        }
        else
        {
            environment.Variables.Add(new AppVariable { Key = name, Value = value ?? "" });
        }
    }

    private static AssertionResult Evaluate(Assertion a, ExecutionResult result, Lazy<JsonNode?> body)
    {
        var (found, actual) = ReadValue(a.Source, a.Target, result, body);
        var subject = Describe(a.Source, a.Target);

        switch (a.Operator)
        {
            case AssertionOperator.Exists:
                return new AssertionResult(found, $"{subject} exists", found ? actual : "(missing)");
            case AssertionOperator.NotExists:
                return new AssertionResult(!found, $"{subject} does not exist", found ? actual : "(missing)");
        }

        if (!found)
        {
            return new AssertionResult(false, $"{subject} {Verb(a.Operator)} \"{a.Expected}\"", "(missing)");
        }

        var passed = a.Operator switch
        {
            AssertionOperator.Equals => string.Equals(actual, a.Expected, StringComparison.Ordinal),
            AssertionOperator.NotEquals => !string.Equals(actual, a.Expected, StringComparison.Ordinal),
            AssertionOperator.Contains => actual?.Contains(a.Expected, StringComparison.OrdinalIgnoreCase) == true,
            AssertionOperator.LessThan => CompareNumbers(actual, a.Expected) is { } lt && lt < 0,
            AssertionOperator.GreaterThan => CompareNumbers(actual, a.Expected) is { } gt && gt > 0,
            _ => false,
        };

        return new AssertionResult(passed, $"{subject} {Verb(a.Operator)} \"{a.Expected}\"", actual);
    }

    /// <summary>Reads the raw value a rule targets. <c>found</c> is false when a JSONPath has no match or
    /// a header is absent; for status/time it is always found.</summary>
    private static (bool Found, string? Value) ReadValue(ResponseField source, string target, ExecutionResult result, Lazy<JsonNode?> body)
    {
        switch (source)
        {
            case ResponseField.StatusCode:
                return (true, result.StatusCode.ToString(CultureInfo.InvariantCulture));

            case ResponseField.ResponseTimeMs:
                return (true, result.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));

            case ResponseField.Header:
                var header = result.Headers.FirstOrDefault(h => string.Equals(h.Key, target, StringComparison.OrdinalIgnoreCase));
                return header is null ? (false, null) : (true, header.Value);

            case ResponseField.JsonBody:
                if (body.Value is null || string.IsNullOrWhiteSpace(target) || !JsonPath.TryParse(target, out var path))
                {
                    return (false, null);
                }

                var match = path.Evaluate(body.Value).Matches.FirstOrDefault();
                if (match is null)
                {
                    return (false, null);
                }

                // Prefer the bare string for a JSON string node ("abc"), else the compact JSON ({...}/123/true).
                var node = match.Value;
                if (node is JsonValue jv && jv.TryGetValue<string>(out var s))
                {
                    return (true, s);
                }

                return (true, node?.ToJsonString() ?? "null");

            default:
                return (false, null);
        }
    }

    private static JsonNode? ParseBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static int? CompareNumbers(string? actual, string? expected)
    {
        if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var e))
        {
            return a.CompareTo(e);
        }

        return null;
    }

    private static string Describe(ResponseField source, string target) => source switch
    {
        ResponseField.StatusCode => "status code",
        ResponseField.ResponseTimeMs => "response time (ms)",
        ResponseField.Header => $"header \"{target}\"",
        ResponseField.JsonBody => $"body {target}",
        _ => "value",
    };

    private static string Verb(AssertionOperator op) => op switch
    {
        AssertionOperator.Equals => "equals",
        AssertionOperator.NotEquals => "does not equal",
        AssertionOperator.Contains => "contains",
        AssertionOperator.LessThan => "is less than",
        AssertionOperator.GreaterThan => "is greater than",
        _ => op.ToString(),
    };
}
