using System.Net.Http;
using Fubar.Studio.Infrastructure.Import;

namespace Fubar.Studio.Infrastructure.Tests.Import;

/// <summary>Focused tests for how the OpenAPI importer materialises parameters and the Accept header:
/// required enabled, optional/deprecated disabled, enum seeding, and response-media Accept.</summary>
public class OpenApiParameterImportTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Params API", "version": "1.0.0" },
      "paths": {
        "/search": {
          "get": {
            "summary": "Search",
            "parameters": [
              { "name": "q", "in": "query", "required": true, "schema": { "type": "string" } },
              { "name": "page", "in": "query", "required": false, "schema": { "type": "integer" } },
              { "name": "legacy", "in": "query", "required": true, "deprecated": true, "schema": { "type": "string" } },
              { "name": "sort", "in": "query", "required": true, "schema": { "type": "string", "enum": ["asc", "desc"] } }
            ],
            "responses": {
              "200": { "description": "ok", "content": { "application/json": { "schema": { "type": "object" } } } }
            }
          }
        }
      }
    }
    """;

    private static async Task<Fubar.Studio.Core.Models.RequestModel> ParseSearchAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fubar-params-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "spec.json");
        await File.WriteAllTextAsync(file, Spec);
        try
        {
            var sut = new OpenApiImportService(new Fubar.Studio.Infrastructure.Workspaces.WorkspaceService(), new StubHttpClientFactory());
            var plan = await sut.ParseAsync(file);
            return plan.Requests.Single().Request;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Required_params_enabled_optional_and_deprecated_disabled()
    {
        var request = await ParseSearchAsync();

        Assert.True(request.QueryParams.Single(p => p.Key == "q").Enabled);
        Assert.False(request.QueryParams.Single(p => p.Key == "page").Enabled);
        Assert.False(request.QueryParams.Single(p => p.Key == "legacy").Enabled); // deprecated -> off despite required
        Assert.Contains("deprecated", request.QueryParams.Single(p => p.Key == "legacy").Description);
    }

    [Fact]
    public async Task Enum_param_without_example_is_seeded_with_first_enum_value()
    {
        var request = await ParseSearchAsync();

        Assert.Equal("asc", request.QueryParams.Single(p => p.Key == "sort").Value);
    }

    [Fact]
    public async Task Accept_header_is_added_from_response_media_types()
    {
        var request = await ParseSearchAsync();

        var accept = request.Headers.Single(h => h.Key == "Accept");
        Assert.Equal("application/json", accept.Value);
        Assert.True(accept.Enabled);
    }

    [Fact]
    public async Task Import_adds_a_status_assertion_and_stashes_the_response_schema()
    {
        var request = await ParseSearchAsync();

        var assertion = Assert.Single(request.Assertions);
        Assert.Equal(Fubar.Studio.Core.Models.ResponseField.StatusCode, assertion.Source);
        Assert.Equal(Fubar.Studio.Core.Models.AssertionOperator.Equals, assertion.Operator);
        Assert.Equal("200", assertion.Expected);

        Assert.NotNull(request.Settings?["fubarOpenApi"]?["responseSchema"]);
    }
}
