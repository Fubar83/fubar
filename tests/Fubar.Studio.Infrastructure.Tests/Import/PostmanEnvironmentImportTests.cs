using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Import;

namespace Fubar.Studio.Infrastructure.Tests.Import;

/// <summary>
/// A Postman environment export is a separate file type from a collection, and the importer could not
/// read one at all - so an imported collection's <c>{{variables}}</c> had nowhere to resolve from.
/// </summary>
public class PostmanEnvironmentImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "fubar-postman-env-" + Guid.NewGuid().ToString("n"));

    public PostmanEnvironmentImportTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task<(RecordingWorkspaceService Store, Core.Import.PostmanImportResult Result)> ImportAsync(string json)
    {
        var path = Path.Combine(_directory, "env.postman_environment.json");
        await File.WriteAllTextAsync(path, json);

        var store = new RecordingWorkspaceService();
        var result = await new PostmanImporter(store).ImportAsync(path, _directory);

        return (store, result);
    }

    [Fact]
    public async Task An_environment_export_becomes_an_environment()
    {
        var (store, result) = await ImportAsync("""
        {
          "id": "abc",
          "name": "Staging",
          "values": [
            { "key": "host", "value": "https://staging.example.com", "type": "default", "enabled": true },
            { "key": "version", "value": "v2", "enabled": true }
          ],
          "_postman_variable_scope": "environment"
        }
        """);

        var environment = Assert.Single(store.SavedEnvironments);
        Assert.Equal("Staging", environment.Name);
        Assert.Equal(2, environment.Variables.Count);
        Assert.Equal("https://staging.example.com", environment.Variables[0].Value);
        Assert.Equal(0, result.RequestCount);
    }

    /// <summary>
    /// The important one. A secret's value lives in the OS keyring; writing it into the committed
    /// environment file on the way in would be the leak the rest of this codebase is arranged to
    /// prevent, performed by the import itself.
    /// </summary>
    [Fact]
    public async Task A_secret_is_imported_without_its_value()
    {
        var (store, result) = await ImportAsync("""
        {
          "name": "Staging",
          "values": [ { "key": "api_key", "value": "sk-live-leaked", "type": "secret", "enabled": true } ],
          "_postman_variable_scope": "environment"
        }
        """);

        var variable = Assert.Single(Assert.Single(store.SavedEnvironments).Variables);

        Assert.Equal(VariableKind.Secret, variable.Kind);
        Assert.Null(variable.Value);
        // And says so, because "your secrets are gone" is not something to discover later.
        Assert.Contains(result.Warnings, w => w.Contains("keyring", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Disabled in the export means the user switched it off rather than deleting it;
    /// importing it as an ordinary variable would silently turn it back on.</summary>
    [Fact]
    public async Task A_disabled_variable_is_skipped_and_reported()
    {
        var (store, result) = await ImportAsync("""
        {
          "name": "Staging",
          "values": [
            { "key": "host", "value": "x", "enabled": true },
            { "key": "old_host", "value": "y", "enabled": false }
          ],
          "_postman_variable_scope": "environment"
        }
        """);

        Assert.Equal("host", Assert.Single(Assert.Single(store.SavedEnvironments).Variables).Key);
        Assert.Contains(result.Warnings, w => w.Contains("old_host", StringComparison.Ordinal));
    }

    /// <summary>A globals export is the same shape and just as useful.</summary>
    [Fact]
    public async Task A_globals_export_is_also_accepted()
    {
        var (store, _) = await ImportAsync("""
        { "name": "Globals", "values": [ { "key": "k", "value": "v" } ], "_postman_variable_scope": "globals" }
        """);

        Assert.Single(store.SavedEnvironments);
    }

    /// <summary>
    /// A malformed COLLECTION must still get the collection error. Detecting an environment by the
    /// absence of "item" alone would silently treat a broken collection as an empty environment.
    /// </summary>
    [Fact]
    public async Task A_malformed_collection_still_reports_as_a_collection()
    {
        var path = Path.Combine(_directory, "broken.json");
        await File.WriteAllTextAsync(path, """{ "info": { "name": "Broken" } }""");

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new PostmanImporter(new RecordingWorkspaceService()).ImportAsync(path, _directory));

        Assert.Contains("collection", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
