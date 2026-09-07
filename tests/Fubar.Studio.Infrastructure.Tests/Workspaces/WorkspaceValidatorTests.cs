using System.Reflection;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>
/// Validating a workspace against the committed schemas is what turns "a workspace is plain files in
/// your repository" into a workflow: a malformed request.json can fail a pull request instead of being
/// found by whoever next opens it.
/// </summary>
public class WorkspaceValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fubar-validate-" + Guid.NewGuid().ToString("n"));

    public WorkspaceValidatorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "collections", "Orders"));
        Directory.CreateDirectory(Path.Combine(_root, "environments"));
        Write("fubar.json", """{ "id": "ws1", "name": "Demo", "variables": [] }""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private void Write(string relative, string json) =>
        File.WriteAllText(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)), json);

    private IReadOnlyList<ValidationProblem> Validate() => new WorkspaceValidator().Validate(_root);

    [Fact]
    public void A_well_formed_workspace_has_no_problems()
    {
        Write("collections/Orders/create.json", """{ "id": "r1", "name": "Create", "method": "POST", "url": "https://x/y" }""");
        Write("environments/staging.json", """{ "id": "e1", "name": "Staging", "variables": [ { "key": "host", "value": "x" } ] }""");

        Assert.Empty(Validate());
    }

    /// <summary>
    /// The first version reported four errors for exactly this file: an if/then branch that did not
    /// apply ("kind is secret, so value must be null" against a normal variable) is recorded as a
    /// failed subschema even though the allOf around it succeeded. It would have failed builds over
    /// nothing.
    /// </summary>
    [Fact]
    public void A_normal_variable_with_a_value_is_not_an_error()
    {
        Write("environments/staging.json", """{ "id": "e1", "name": "S", "variables": [ { "key": "host", "value": "x", "kind": "normal" } ] }""");

        Assert.DoesNotContain(Validate(), p => p.IsError);
    }

    /// <summary>The T1.1 leak, as a pull-request gate: a secret's value must not be on disk.</summary>
    [Fact]
    public void A_secret_with_a_value_on_disk_is_an_error()
    {
        Write("environments/staging.json", """{ "id": "e1", "name": "S", "variables": [ { "key": "t", "value": "leaked", "kind": "secret" } ] }""");

        Assert.Contains(Validate(), p => p.IsError);
    }

    [Fact]
    public void A_wrongly_typed_field_is_an_error()
    {
        Write("collections/Orders/create.json", """{ "name": "Create", "method": 42 }""");

        var problem = Assert.Single(Validate(), p => p.Location == "/method");
        Assert.True(problem.IsError);
    }

    [Fact]
    public void An_unknown_enum_value_is_an_error()
    {
        Write("collections/Orders/create.json",
            """{ "name": "C", "captures": [ { "variableName": "t", "scope": "nonsense" } ] }""");

        Assert.Contains(Validate(), p => p.IsError && p.Location.StartsWith("/captures", StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_json_is_reported_as_such()
    {
        Write("collections/Orders/create.json", "{ not json");

        Assert.Contains(Validate(), p => p.IsError && p.Message.Contains("Not valid JSON", StringComparison.Ordinal));
    }

    /// <summary>
    /// A warning, not an error: a deliberately public sandbox key is legitimate, and failing someone's
    /// build over it would be wrong about the case they understand better than this does.
    /// </summary>
    [Fact]
    public void A_credential_shaped_value_in_a_committed_file_warns()
    {
        Write("environments/staging.json", """{ "id": "e1", "name": "S", "variables": [ { "key": "api_token", "value": "sk-live-x" } ] }""");

        var problem = Assert.Single(Validate());
        Assert.False(problem.IsError);
        Assert.Contains("api_token", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>Execution history is local scratch state, not part of the format - and validating a
    /// hundred response ledgers on every run would be slow and pointless.</summary>
    [Fact]
    public void Execution_history_is_not_validated()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".fubar", "history"));
        Write(".fubar/history/r1.json", "{ not json at all");

        Assert.Empty(Validate());
    }

    [Theory]
    [InlineData("fubar.json", WorkspaceFileKind.Manifest)]
    [InlineData("auth-profiles.json", WorkspaceFileKind.AuthProfiles)]
    [InlineData("collections/Orders/create.json", WorkspaceFileKind.Request)]
    [InlineData("collections/Orders/_folder.json", WorkspaceFileKind.FolderConfig)]
    [InlineData("environments/staging.json", WorkspaceFileKind.Environment)]
    [InlineData("package.json", WorkspaceFileKind.Unknown)]
    public void A_file_is_recognised_from_its_path(string relative, WorkspaceFileKind expected)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(expected, WorkspaceFiles.KindOf(_root, path));
    }

    /// <summary>
    /// The drift guard. A property added to a model without a matching schema entry means the schema
    /// silently stops describing the format - and the editors and CI gates built on it start passing
    /// documents nobody checked.
    /// </summary>
    [Theory]
    [InlineData(typeof(RequestModel), "request.schema.json")]
    [InlineData(typeof(AppManifest), "app.schema.json")]
    [InlineData(typeof(WorkspaceEnvironment), "environment.schema.json")]
    [InlineData(typeof(FolderConfig), "folder.schema.json")]
    [InlineData(typeof(AppVariable), "environment.schema.json")]
    [InlineData(typeof(TransportSettings), "environment.schema.json")]
    [InlineData(typeof(KeyValueItem), "request.schema.json")]
    [InlineData(typeof(CaptureRule), "request.schema.json")]
    [InlineData(typeof(Assertion), "request.schema.json")]
    [InlineData(typeof(ComparisonSettings), "request.schema.json")]
    public void Every_model_property_appears_in_its_schema(Type model, string schemaFile)
    {
        var schema = File.ReadAllText(Path.Combine(SchemasDirectory(), schemaFile));

        var missing = model
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite || p.PropertyType.IsGenericType)
            // IsSecret is a write-only back-compat shim whose getter returns null, so it is never
            // serialized and has nothing to describe.
            .Where(p => p.Name != "IsSecret")
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .Where(name => !schema.Contains($"\"{name}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{model.Name} has properties the schema does not describe: {string.Join(", ", missing)}. "
            + $"Add them to schemas/v1/{schemaFile}, or the schema stops describing the format.");
    }

    private static string SchemasDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "schemas", "v1");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate schemas/v1 by walking up from the test output.");
    }
}
