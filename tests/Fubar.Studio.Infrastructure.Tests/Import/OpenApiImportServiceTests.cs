using System.Net.Http;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Import;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Import;

/// <summary>Imports representative OpenAPI 3.x / Swagger 2.0 specs into a real temp workspace and
/// asserts the requests / environments / auth profiles / variables it materialises.</summary>
public class OpenApiImportServiceTests : IDisposable
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private readonly string _root;
    private readonly WorkspaceService _ws = new();
    private readonly OpenApiImportService _sut;

    public OpenApiImportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fubar-openapi-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _sut = new OpenApiImportService(_ws, new StubHttpClientFactory());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private const string PetStoreSpec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Pet Store", "version": "1.0.0" },
      "servers": [
        { "url": "https://api.example.com/v1", "description": "Production" },
        { "url": "https://staging.example.com/v1", "description": "Staging" }
      ],
      "security": [ { "bearerAuth": [] } ],
      "paths": {
        "/pets": {
          "get": {
            "tags": ["pets"],
            "summary": "List pets",
            "parameters": [ { "name": "limit", "in": "query", "schema": { "type": "integer", "default": 20 } } ]
          },
          "post": {
            "tags": ["pets"],
            "summary": "Create a pet",
            "requestBody": {
              "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } }
            }
          }
        },
        "/pets/{petId}": {
          "get": {
            "tags": ["pets"],
            "summary": "Get a pet",
            "security": [],
            "parameters": [ { "name": "petId", "in": "path", "required": true, "schema": { "type": "string" } } ]
          }
        }
      },
      "components": {
        "securitySchemes": {
          "bearerAuth": { "type": "http", "scheme": "bearer" },
          "apiKeyAuth": { "type": "apiKey", "in": "header", "name": "X-API-Key" }
        },
        "schemas": {
          "Pet": {
            "type": "object",
            "properties": {
              "name": { "type": "string", "example": "Rex" },
              "tag": { "type": "string" }
            }
          }
        }
      }
    }
    """;

    private async Task<string> WriteSpecAndInitWorkspaceAsync()
    {
        await _ws.SaveAppManifestAsync(_root, new AppManifest { Name = "WS" });
        Directory.CreateDirectory(Path.Combine(_root, "collections"));
        var specPath = Path.Combine(_root, "petstore.json");
        await File.WriteAllTextAsync(specPath, PetStoreSpec);
        return specPath;
    }

    [Fact]
    public async Task Import_CreatesRequestsEnvironmentsAndAuthProfiles()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();

        var result = await _sut.ImportAsync(spec, _root);

        Assert.Equal("Pet Store", result.ApiTitle);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal(2, result.EnvironmentCount);       // Production + Staging
        // bearerAuth only. apiKeyAuth is DECLARED and never referenced by any operation, so importing a
        // profile and a credential variable for it would be furnishing auth nobody asked for.
        Assert.Equal(1, result.AuthProfileCount);
        Assert.Empty(result.Warnings);
        Assert.True(Directory.Exists(Path.Combine(_root, "collections", "Pet Store", "pets")));
    }

    [Fact]
    public async Task Import_MapsUrlPathParamsAndPerOperationSecurity()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();
        await _sut.ImportAsync(spec, _root);

        var getPet = await LoadRequestByNameAsync("Get a pet");

        Assert.Equal("GET", getPet.Method);
        // The spec's own {petId}, NOT {{petId}}. A path parameter is never an environment variable: the
        // names collide across unrelated resources (/users/{id} and /orders/{id} would share one "id"),
        // and it belongs to the one request whose URL contains it. Single braces are inert to the
        // resolver, so this reads as the placeholder it is rather than an undefined variable.
        Assert.Equal("{{baseUrl}}/pets/{petId}", getPet.Url);
        Assert.Equal(AuthType.None, getPet.Auth.Type); // "security": [] on the operation
    }

    [Fact]
    public async Task Import_UsesGlobalSecurityAndQueryParams()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();
        await _sut.ImportAsync(spec, _root);

        var listPets = await LoadRequestByNameAsync("List pets");
        var profiles = await _ws.LoadAuthProfilesAsync(_root);
        var bearer = profiles.Single(p => p.Name == "bearerAuth");

        Assert.Equal(AuthType.Profile, listPets.Auth.Type);
        Assert.Equal(bearer.Id, listPets.AuthProfileId);
        Assert.Contains(listPets.QueryParams, q => q.Key == "limit" && q.Value == "20");
    }

    [Fact]
    public async Task Import_StoresSelfContainedBodySchema_ForValidation()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();
        await _sut.ImportAsync(spec, _root);

        var createPet = await LoadRequestByNameAsync("Create a pet");
        var schema = createPet.Settings?["fubarOpenApi"]?["bodySchema"];

        Assert.NotNull(schema);
        var schemaJson = schema!.ToJsonString();
        Assert.Contains("$defs", schemaJson);      // component schemas bundled in
        Assert.Contains("\"name\"", schemaJson);    // Pet.name property

        // And it actually drives validation: a Pet missing the (schema-typed) fields still validates,
        // but a wrong-typed field is caught.
        Assert.NotEmpty(new Fubar.Studio.Infrastructure.Json.JsonSchemaValidator().Validate(schemaJson, """{ "name": 123 }"""));
    }

    [Fact]
    public async Task Import_SynthesizesJsonBodyFromSchema()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();
        await _sut.ImportAsync(spec, _root);

        var createPet = await LoadRequestByNameAsync("Create a pet");

        Assert.Equal(BodyType.Json, createPet.Body.Type);
        Assert.Contains("\"name\"", createPet.Body.Raw);
        Assert.Contains("Rex", createPet.Body.Raw); // property example flowed through
    }

    [Fact]
    public async Task Import_PutsBaseUrlAndSecretsIntoEachEnvironment()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();
        await _sut.ImportAsync(spec, _root);

        var environments = await _ws.LoadEnvironmentsAsync(_root);
        var production = environments.Single(e => e.Name == "Production");

        Assert.Equal("https://api.example.com/v1", production.Variables.Single(v => v.Key == "baseUrl").Value);
        Assert.DoesNotContain(production.Variables, v => v.Key == "petId");                 // path params are never variables
        Assert.Contains(production.Variables, v => v.Key == "bearerToken_bearerAuth" && v.Kind == VariableKind.Secret); // auth secret

        // The first imported environment becomes active so {{baseUrl}} resolves immediately.
        var workspace = await _ws.LoadWorkspaceAsync(_root);
        Assert.Contains(environments, e => e.Id == workspace.Manifest.ActiveEnvironmentId);
    }

    [Fact]
    public async Task Import_MapsQueryParamAsPlaceholderWithRequiredAndDescription()
    {
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Params API" },
          "servers": [ { "url": "https://p.example.com" } ],
          "paths": {
            "/search": {
              "get": {
                "summary": "Search",
                "parameters": [
                  { "name": "q", "in": "query", "required": true, "description": "Search text", "schema": { "type": "string" } },
                  { "name": "sort", "in": "query", "schema": { "type": "string", "enum": ["asc", "desc"] } }
                ]
              }
            }
          }
        }
        """;
        await _ws.SaveAppManifestAsync(_root, new AppManifest { Name = "WS" });
        Directory.CreateDirectory(Path.Combine(_root, "collections"));
        var specPath = Path.Combine(_root, "params.json");
        await File.WriteAllTextAsync(specPath, spec);

        await _sut.ImportAsync(specPath, _root);
        var search = await LoadRequestByNameAsync("Search");

        var q = search.QueryParams.Single(p => p.Key == "q");
        Assert.True(q.Enabled);                       // required -> checked
        Assert.Equal("<string>", q.Value);            // placeholder
        Assert.StartsWith("required", q.Description);
        Assert.Contains("Search text", q.Description);

        var sort = search.QueryParams.Single(p => p.Key == "sort");
        Assert.False(sort.Enabled);                   // optional -> unchecked
        Assert.Contains("one of: asc, desc", sort.Description);
    }

    [Fact]
    public async Task Reimport_UpdatesExistingRequestsInPlace()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();

        var first = await _sut.ImportAsync(spec, _root);
        Assert.Equal(3, first.CreatedCount);
        Assert.Equal(0, first.UpdatedCount);

        var before = Directory.EnumerateFiles(Path.Combine(_root, "collections"), "*.json", SearchOption.AllDirectories).Count();
        var listPetsId = (await LoadRequestByNameAsync("List pets")).Id;

        // Re-import the same spec: should update in place, not duplicate.
        var second = await _sut.ImportAsync(spec, _root);
        var after = Directory.EnumerateFiles(Path.Combine(_root, "collections"), "*.json", SearchOption.AllDirectories).Count();

        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(3, second.UpdatedCount);
        Assert.Equal(before, after);                                  // no new files
        Assert.Equal(listPetsId, (await LoadRequestByNameAsync("List pets")).Id); // same id preserved
        Assert.False(Directory.Exists(Path.Combine(_root, "collections", "Pet Store 2"))); // no duplicate folder
    }

    private const string SwaggerV2Spec = """
    {
      "swagger": "2.0",
      "info": { "title": "Legacy API", "version": "1.0" },
      "host": "legacy.example.com",
      "basePath": "/api",
      "schemes": ["https"],
      "securityDefinitions": {
        "apiKey": { "type": "apiKey", "in": "header", "name": "X-Key" },
        "basicAuth": { "type": "basic" }
      },
      "paths": {
        "/items": {
          "get": {
            "tags": ["items"],
            "summary": "List items",
            "parameters": [ { "name": "page", "in": "query", "type": "integer", "default": 1 } ]
          },
          "post": {
            "tags": ["items"],
            "summary": "Create item",
            "parameters": [ { "name": "body", "in": "body", "schema": { "$ref": "#/definitions/Item" } } ]
          }
        },
        "/items/{id}": {
          "get": {
            "tags": ["items"],
            "summary": "Get item",
            "parameters": [ { "name": "id", "in": "path", "required": true, "type": "string" } ]
          }
        }
      },
      "definitions": {
        "Item": { "type": "object", "properties": { "title": { "type": "string", "example": "Widget" } } }
      }
    }
    """;

    [Fact]
    public async Task Import_HandlesSwagger2_ServersSecurityAndBody()
    {
        await _ws.SaveAppManifestAsync(_root, new AppManifest { Name = "WS" });
        Directory.CreateDirectory(Path.Combine(_root, "collections"));
        var specPath = Path.Combine(_root, "legacy.json");
        await File.WriteAllTextAsync(specPath, SwaggerV2Spec);

        var result = await _sut.ImportAsync(specPath, _root);

        Assert.Equal("Legacy API", result.ApiTitle);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal(1, result.EnvironmentCount);   // single synthesized server (https + host + basePath)
        Assert.Equal(2, result.AuthProfileCount);   // apiKey + basicAuth

        var environments = await _ws.LoadEnvironmentsAsync(_root);
        Assert.Equal("https://legacy.example.com/api", environments[0].Variables.Single(v => v.Key == "baseUrl").Value);

        var listItems = await LoadRequestByNameAsync("List items");
        Assert.Contains(listItems.QueryParams, q => q.Key == "page" && q.Value == "1"); // v2 default on the param

        var createItem = await LoadRequestByNameAsync("Create item");
        Assert.Equal(BodyType.Json, createItem.Body.Type);
        Assert.Contains("Widget", createItem.Body.Raw); // body param schema $ref -> #/definitions/Item
    }

    [Fact]
    public async Task Import_ParsesYamlSpecs()
    {
        const string yaml = """
        openapi: 3.0.0
        info:
          title: YAML API
        servers:
          - url: https://yaml.example.com
        paths:
          /ping:
            get:
              summary: Ping
        """;
        await _ws.SaveAppManifestAsync(_root, new AppManifest { Name = "WS" });
        Directory.CreateDirectory(Path.Combine(_root, "collections"));
        var specPath = Path.Combine(_root, "api.yaml");
        await File.WriteAllTextAsync(specPath, yaml);

        var result = await _sut.ImportAsync(specPath, _root);

        Assert.Equal("YAML API", result.ApiTitle);
        Assert.Equal(1, result.RequestCount);
        var ping = await LoadRequestByNameAsync("Ping");
        Assert.Equal("{{baseUrl}}/ping", ping.Url);
    }

    [Fact]
    public async Task Import_ResolvesRefParametersAndAllOfBodies()
    {
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Ref API" },
          "servers": [ { "url": "https://ref.example.com" } ],
          "paths": {
            "/things": {
              "get": {
                "summary": "List things",
                "parameters": [ { "$ref": "#/components/parameters/PageParam" } ]
              },
              "post": {
                "summary": "Create thing",
                "requestBody": {
                  "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Thing" } } }
                }
              }
            }
          },
          "components": {
            "parameters": {
              "PageParam": { "name": "page", "in": "query", "schema": { "type": "integer", "default": 5 } }
            },
            "schemas": {
              "Base": { "type": "object", "properties": { "id": { "type": "string", "example": "abc" } } },
              "Thing": {
                "allOf": [
                  { "$ref": "#/components/schemas/Base" },
                  { "type": "object", "properties": { "label": { "type": "string", "example": "hello" } } }
                ]
              }
            }
          }
        }
        """;
        await _ws.SaveAppManifestAsync(_root, new AppManifest { Name = "WS" });
        Directory.CreateDirectory(Path.Combine(_root, "collections"));
        var specPath = Path.Combine(_root, "ref.json");
        await File.WriteAllTextAsync(specPath, spec);

        await _sut.ImportAsync(specPath, _root);

        var list = await LoadRequestByNameAsync("List things");
        Assert.Contains(list.QueryParams, q => q.Key == "page" && q.Value == "5"); // $ref parameter resolved

        var create = await LoadRequestByNameAsync("Create thing");
        Assert.Contains("\"id\"", create.Body.Raw);    // from the allOf base schema
        Assert.Contains("\"label\"", create.Body.Raw); // from the allOf extension
    }

    [Fact]
    public async Task ParseThenApply_HonoursOptions()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();

        var plan = await _sut.ParseAsync(spec);
        Assert.Equal("Pet Store", plan.ApiTitle);
        Assert.Equal(3, plan.Requests.Count);
        Assert.Equal(2, plan.Environments.Count);

        // Apply without environments or auth profiles.
        await _sut.ApplyAsync(plan, _root, new OpenApiImportOptions { CreateEnvironments = false, CreateAuthProfiles = false });

        Assert.Empty(await _ws.LoadEnvironmentsAsync(_root));
        Assert.Empty(await _ws.LoadAuthProfilesAsync(_root));
        var getPet = await LoadRequestByNameAsync("Get a pet");
        Assert.Equal(AuthType.None, getPet.Auth.Type); // was already None; profile refs would be stripped
        var listPets = await LoadRequestByNameAsync("List pets");
        Assert.Equal(AuthType.Inherit, listPets.Auth.Type); // profile ref stripped since auth wasn't created
    }

    [Fact]
    public async Task Diff_FirstImport_MarksEverythingAdd()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();

        var plan = await _sut.ParseAsync(spec);
        var diff = await _sut.DiffAsync(plan, _root);

        Assert.All(diff.Requests, r => Assert.Equal(ImportChange.Add, r.Change));
        Assert.Contains(diff.Variables, v => v is { Key: "baseUrl", Change: ImportChange.Add });
    }

    [Fact]
    public async Task Diff_Reimport_IsUnchanged_And_SelectiveApply_PreservesManualEdits()
    {
        var spec = await WriteSpecAndInitWorkspaceAsync();
        await _sut.ImportAsync(spec, _root); // first import

        // A manual edit the user made after importing.
        var listPath = FindRequestFile("List pets");
        var edited = await _ws.LoadRequestAsync(listPath);
        edited.Url = "{{baseUrl}}/pets?manual=1";
        await _ws.SaveRequestAsync(listPath, edited);

        var plan = await _sut.ParseAsync(spec);
        var diff = await _sut.DiffAsync(plan, _root);

        Assert.Equal(ImportChange.Update, diff.Requests.Single(r => r.DisplayName == "List pets").Change);
        Assert.Equal(ImportChange.Unchanged, diff.Requests.Single(r => r.DisplayName == "Get a pet").Change);

        // Re-apply everything EXCEPT the manually edited request.
        var selected = diff.Requests.Where(r => r.Change != ImportChange.Unchanged && r.DisplayName != "List pets").ToList();
        await _sut.ApplyDiffAsync(plan, selected, [], OpenApiImportOptions.Default, _root);

        Assert.Equal("{{baseUrl}}/pets?manual=1", (await _ws.LoadRequestAsync(listPath)).Url); // survived
    }

    [Fact]
    public async Task Diff_DetectsRemovedRequests_AndApplyDeletesSelected()
    {
        const string twoOps = """
        { "openapi": "3.0.0", "info": { "title": "Mini" },
          "servers": [ { "url": "https://m.example.com" } ],
          "paths": {
            "/a": { "get": { "summary": "Op A" } },
            "/b": { "get": { "summary": "Op B" } } } }
        """;
        const string oneOp = """
        { "openapi": "3.0.0", "info": { "title": "Mini" },
          "servers": [ { "url": "https://m.example.com" } ],
          "paths": { "/a": { "get": { "summary": "Op A" } } } }
        """;
        await _ws.SaveAppManifestAsync(_root, new AppManifest { Name = "WS" });
        Directory.CreateDirectory(Path.Combine(_root, "collections"));
        await File.WriteAllTextAsync(Path.Combine(_root, "two.json"), twoOps);
        await File.WriteAllTextAsync(Path.Combine(_root, "one.json"), oneOp);

        await _sut.ImportAsync(Path.Combine(_root, "two.json"), _root); // has Op A + Op B

        var plan = await _sut.ParseAsync(Path.Combine(_root, "one.json")); // only Op A
        var diff = await _sut.DiffAsync(plan, _root);

        var removed = diff.Requests.Single(r => r.Change == ImportChange.Remove);
        Assert.Equal("Op B", removed.DisplayName);

        await _sut.ApplyDiffAsync(plan, [removed], [], OpenApiImportOptions.Default, _root);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.Combine(_root, "collections"), "*.json", SearchOption.AllDirectories),
            f => Path.GetFileNameWithoutExtension(f) == "Op B");
    }

    private string FindRequestFile(string requestName) =>
        Directory.EnumerateFiles(Path.Combine(_root, "collections"), "*.json", SearchOption.AllDirectories)
            .Single(f => Path.GetFileNameWithoutExtension(f) == requestName);

    private async Task<RequestModel> LoadRequestByNameAsync(string requestName)
    {
        var file = Directory
            .EnumerateFiles(Path.Combine(_root, "collections"), "*.json", SearchOption.AllDirectories)
            .Single(f => Path.GetFileNameWithoutExtension(f) == requestName);
        return await _ws.LoadRequestAsync(file);
    }
}
