using Fubar.Studio.Core.Import;
using Fubar.Studio.Infrastructure.Import;

namespace Fubar.Studio.Infrastructure.Tests.Import;

/// <summary>
/// A Postman collection now produces the same <see cref="ImportPlan"/> an OpenAPI spec does, so it goes
/// through the same preview.
///
/// <para>It used to be written straight into the workspace, with the outcome reported into a status log
/// that was collapsed by default - so re-importing a collection silently overwrote whatever had been
/// edited since, and the only notice was somewhere nobody was looking.</para>
/// </summary>
public class PostmanPlanTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "fubar-postman-plan-" + Guid.NewGuid().ToString("n"));

    public PostmanPlanTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task<ImportPlan> PlanAsync(string json)
    {
        var path = Path.Combine(_directory, "collection.json");
        await File.WriteAllTextAsync(path, json);

        return await new PostmanImporter(new RecordingWorkspaceService()).ParseAsync(path);
    }

    private const string Collection = """
    {
      "info": { "name": "Orders API" },
      "item": [
        {
          "name": "Orders",
          "item": [
            {
              "name": "Create order",
              "request": { "method": "POST", "url": { "raw": "https://api.example.com/orders" } },
              "event": [
                {
                  "listen": "test",
                  "script": { "exec": ["pm.response.to.have.status(201);"] }
                }
              ]
            }
          ]
        },
        {
          "name": "Health",
          "request": { "method": "GET", "url": { "raw": "https://api.example.com/health" } }
        }
      ],
      "variable": [ { "key": "host", "value": "api.example.com" } ]
    }
    """;

    [Fact]
    public async Task A_collection_becomes_a_plan_without_writing_anything()
    {
        var store = new RecordingWorkspaceService();
        var path = Path.Combine(_directory, "c.json");
        await File.WriteAllTextAsync(path, Collection);

        var plan = await new PostmanImporter(store).ParseAsync(path);

        Assert.Equal("Orders API", plan.ApiTitle);
        Assert.Equal(2, plan.Requests.Count);
        // Nothing on disk - that is the whole point of a plan.
        Assert.Empty(store.SavedRequests);
        Assert.Empty(store.SavedEnvironments);
    }

    /// <summary>Postman nests folders arbitrarily; the structure has to survive rather than flatten.</summary>
    [Fact]
    public async Task Folder_structure_is_carried_into_the_plan()
    {
        var plan = await PlanAsync(Collection);

        Assert.Equal("Orders", plan.Requests.Single(r => r.Request.Name == "Create order").FolderName);
        // A request at the top level belongs to no folder.
        Assert.Equal("", plan.Requests.Single(r => r.Request.Name == "Health").FolderName);
    }

    [Fact]
    public async Task Translated_assertions_survive_into_the_plan()
    {
        var plan = await PlanAsync(Collection);

        var assertion = Assert.Single(plan.Requests.Single(r => r.Request.Name == "Create order").Request.Assertions);
        Assert.Equal("201", assertion.Expected);
    }

    [Fact]
    public async Task Collection_variables_become_an_environment_in_the_plan()
    {
        var plan = await PlanAsync(Collection);

        var environment = Assert.Single(plan.Environments);
        Assert.Equal("host", Assert.Single(environment.Variables).Key);
    }

    /// <summary>An environment export has no requests, so a preview would list nothing and read as a
    /// failed parse. It is a separate action, and parsing one as a collection says so.</summary>
    [Fact]
    public async Task An_environment_export_is_refused_by_the_planner_with_a_useful_message()
    {
        var path = Path.Combine(_directory, "env.json");
        await File.WriteAllTextAsync(path, """{ "name": "Staging", "values": [], "_postman_variable_scope": "environment" }""");

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new PostmanImporter(new RecordingWorkspaceService()).ParseAsync(path));

        Assert.Contains("import it directly", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_planner_describes_itself_for_the_dialog()
    {
        var planner = new PostmanImporter(new RecordingWorkspaceService());

        Assert.Equal("Postman collection", planner.SourceDescription);
        // A Postman export is a file you downloaded, not a URL you subscribe to.
        Assert.False(planner.AcceptsUrl);
    }
}
