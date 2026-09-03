using System.Net.Http;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Import;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Import;

/// <summary>
/// What an import must NOT create.
///
/// An import of eight operations used to materialise fourteen environment variables per environment, of
/// which three were correct: credentials for security schemes nothing referenced, one shared variable per
/// distinct path parameter name (so unrelated resources fought over "id"), and copies of server variables
/// that were inert and landed in environments they did not belong to. One of the four also broke auth
/// silently. These pin each of those shut.
/// </summary>
public class OpenApiImportNoiseTests : IDisposable
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private readonly string _root;
    private readonly OpenApiImportService _sut = new(new WorkspaceService(), new StubHttpClientFactory());

    public OpenApiImportNoiseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fubar-import-noise-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task<Core.Import.OpenApiImportPlan> PlanAsync(string spec)
    {
        var path = Path.Combine(_root, "spec.json");
        await File.WriteAllTextAsync(path, spec);
        return await _sut.ParseAsync(path);
    }

    // ---- Path parameters are never variables ---------------------------------------------------

    private const string CollidingPathParams = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Shop", "version": "1.0.0" },
      "servers": [ { "url": "https://api.example.com" } ],
      "paths": {
        "/users/{id}":        { "get": { "summary": "Get user",  "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ] } },
        "/users/{id}/orders": { "get": { "summary": "User orders", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ] } },
        "/orders/{id}":       { "get": { "summary": "Get order",  "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ] } },
        "/pets/{petId}":      { "get": { "summary": "Get pet",    "parameters": [ { "name": "petId", "in": "path", "required": true, "schema": { "type": "integer" }, "example": 42 } ] } }
      }
    }
    """;

    [Fact]
    public async Task No_path_parameter_becomes_an_environment_variable()
    {
        // The rule, stated flatly. The old behaviour made one workspace-wide variable per distinct {name},
        // which is wrong at scale AND wrong in kind - see the collision test below.
        var plan = await PlanAsync(CollidingPathParams);

        var keys = plan.Environments.Single().Variables.Select(v => v.Key).ToList();

        Assert.Equal(["baseUrl"], keys);
    }

    [Fact]
    public async Task Colliding_names_no_longer_share_one_variable()
    {
        // /users/{id}, /users/{id}/orders and /orders/{id} used to resolve to a single "id", so filling it
        // in for one request broke the other two. Each request now carries its own placeholder.
        var plan = await PlanAsync(CollidingPathParams);

        Assert.Equal("{{baseUrl}}/users/{id}", Url(plan, "Get user"));
        Assert.Equal("{{baseUrl}}/users/{id}/orders", Url(plan, "User orders"));
        Assert.Equal("{{baseUrl}}/orders/{id}", Url(plan, "Get order"));
    }

    [Fact]
    public async Task A_path_parameter_with_an_example_is_substituted_so_the_request_is_runnable()
    {
        var plan = await PlanAsync(CollidingPathParams);

        Assert.Equal("{{baseUrl}}/pets/42", Url(plan, "Get pet"));
    }

    [Fact]
    public async Task A_path_parameter_without_one_keeps_the_specs_own_name()
    {
        // Not "<string>": /users/<string>/orders/<string> throws away which parameter is which, and the
        // name is the only thing telling the reader what to put there. Single braces are inert to the
        // variable resolver, so it reads as a placeholder rather than an undefined variable.
        Assert.Contains("{id}", Url(await PlanAsync(CollidingPathParams), "Get user"));
    }

    // ---- Only referenced security schemes ------------------------------------------------------

    private const string ManySchemes = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Shop", "version": "1.0.0" },
      "servers": [ { "url": "https://api.example.com" } ],
      "security": [ { "bearerAuth": [] } ],
      "components": {
        "securitySchemes": {
          "bearerAuth": { "type": "http", "scheme": "bearer" },
          "apiKeyAuth": { "type": "apiKey", "name": "X-API-Key", "in": "header" },
          "basicAuth":  { "type": "http", "scheme": "basic" },
          "oauth2Auth": { "type": "oauth2", "flows": { "clientCredentials": { "tokenUrl": "https://x/token", "scopes": {} } } }
        }
      },
      "paths": { "/users": { "get": { "summary": "List users" } } }
    }
    """;

    [Fact]
    public async Task A_declared_but_unreferenced_scheme_creates_no_profile_and_no_credential()
    {
        // Four declared, one referenced. The other three used to contribute four variables - including a
        // basic auth username and password - for auth nobody asked for.
        var plan = await PlanAsync(ManySchemes);

        Assert.Equal(["bearerAuth"], plan.AuthProfiles.Select(p => p.Name));
        Assert.Equal(
            ["baseUrl", "bearerToken_bearerAuth"],
            plan.Environments.Single().Variables.Select(v => v.Key));
    }

    [Fact]
    public async Task An_operations_own_security_counts_as_a_reference()
    {
        // Referenced only by one operation, never globally - still used.
        var plan = await PlanAsync("""
        {
          "openapi": "3.0.3",
          "info": { "title": "S", "version": "1" },
          "servers": [ { "url": "https://x" } ],
          "components": { "securitySchemes": {
            "bearerAuth": { "type": "http", "scheme": "bearer" },
            "apiKeyAuth": { "type": "apiKey", "name": "X-API-Key", "in": "header" } } },
          "paths": { "/a": { "get": { "summary": "A", "security": [ { "apiKeyAuth": [] } ] } } }
        }
        """);

        Assert.Equal(["apiKeyAuth"], plan.AuthProfiles.Select(p => p.Name));
    }

    [Fact]
    public async Task A_spec_that_references_nothing_keeps_every_scheme_and_says_why()
    {
        // The spec declares auth and never wires it up. Importing none would leave nothing to switch on,
        // so all are kept - and the warning tells the user to delete what they do not need.
        var plan = await PlanAsync("""
        {
          "openapi": "3.0.3",
          "info": { "title": "S", "version": "1" },
          "servers": [ { "url": "https://x" } ],
          "components": { "securitySchemes": {
            "bearerAuth": { "type": "http", "scheme": "bearer" },
            "apiKeyAuth": { "type": "apiKey", "name": "X-API-Key", "in": "header" } } },
          "paths": { "/a": { "get": { "summary": "A" } } }
        }
        """);

        Assert.Equal(2, plan.AuthProfiles.Count);
        Assert.Contains(plan.Warnings, w => w.Contains("No operation references a security scheme"));
    }

    // ---- Server variables ----------------------------------------------------------------------

    private const string ServerVariables = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Shop", "version": "1.0.0" },
      "servers": [
        { "url": "https://{region}.api.example.com/{version}", "description": "Production",
          "variables": { "region": { "default": "us" }, "version": { "default": "v2" } } },
        { "url": "https://staging.example.com/v2", "description": "Staging" }
      ],
      "paths": { "/users": { "get": { "summary": "List users" } } }
    }
    """;

    [Fact]
    public async Task Server_variables_are_substituted_into_the_url_and_not_also_copied_as_variables()
    {
        // They used to be both, which made them INERT: baseUrl already held the resolved URL, so nothing
        // referenced them and setting region to "eu" changed nothing at all.
        var plan = await PlanAsync(ServerVariables);
        var production = plan.Environments.Single(e => e.Name == "Production");

        Assert.Equal("https://us.api.example.com/v2", production.Variables.Single(v => v.Key == "baseUrl").Value);
        Assert.Equal(["baseUrl"], production.Variables.Select(v => v.Key));
    }

    [Fact]
    public async Task One_servers_variables_do_not_leak_into_another_servers_environment()
    {
        // Staging's URL is literal and has no variables of its own; it used to receive Production's,
        // carrying Production's values.
        var plan = await PlanAsync(ServerVariables);
        var staging = plan.Environments.Single(e => e.Name == "Staging");

        Assert.DoesNotContain(staging.Variables, v => v.Key is "region" or "version");
        Assert.Equal("https://staging.example.com/v2", staging.Variables.Single(v => v.Key == "baseUrl").Value);
    }

    // ---- A declared parameter must not silently suppress the auth ------------------------------

    [Fact]
    public async Task An_Authorization_header_parameter_is_imported_unchecked_so_it_cannot_suppress_the_token()
    {
        // The silent one. Imported enabled with a placeholder, AuthRequestMerge - correctly - refuses to
        // overwrite a header the request already carries enabled, so "<string>" went out as the
        // Authorization header and the bearer token never did. 401s that look like the profile is broken.
        var plan = await PlanAsync("""
        {
          "openapi": "3.0.3",
          "info": { "title": "S", "version": "1" },
          "servers": [ { "url": "https://x" } ],
          "security": [ { "bearerAuth": [] } ],
          "components": { "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } } },
          "paths": { "/carts": { "get": { "summary": "Get cart", "parameters": [
            { "name": "Authorization", "in": "header", "required": true, "schema": { "type": "string" } },
            { "name": "X-Request-Id",  "in": "header", "required": true, "schema": { "type": "string" } } ] } } }
        }
        """);

        var request = plan.Requests.Single().Request;
        var authorization = request.Headers.Single(h => h.Key == "Authorization");

        Assert.False(authorization.Enabled);
        Assert.Contains(plan.Warnings, w => w.Contains("Authorization") && w.Contains("suppress"));

        // Unrelated required headers are untouched - only the one the auth would send is disabled.
        Assert.True(request.Headers.Single(h => h.Key == "X-Request-Id").Enabled);
    }

    [Fact]
    public async Task The_disabled_header_really_does_let_the_token_through()
    {
        // Asserting the CONSEQUENCE rather than the flag: this is the behaviour the fix exists for.
        var plan = await PlanAsync("""
        {
          "openapi": "3.0.3",
          "info": { "title": "S", "version": "1" },
          "servers": [ { "url": "https://x" } ],
          "security": [ { "bearerAuth": [] } ],
          "components": { "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } } },
          "paths": { "/carts": { "get": { "summary": "Get cart", "parameters": [
            { "name": "Authorization", "in": "header", "required": true, "schema": { "type": "string" } } ] } } }
        }
        """);

        var merged = AuthRequestMerge.Inject(
            plan.Requests.Single().Request,
            new AppliedAuth([new KeyValueItem { Key = "Authorization", Value = "Bearer real-token" }], []));

        Assert.Contains(merged.Headers, h => h.Enabled && h.Value == "Bearer real-token");
    }

    [Fact]
    public async Task An_api_key_query_parameter_the_scheme_sends_is_disabled_too()
    {
        var plan = await PlanAsync("""
        {
          "openapi": "3.0.3",
          "info": { "title": "S", "version": "1" },
          "servers": [ { "url": "https://x" } ],
          "security": [ { "apiKeyAuth": [] } ],
          "components": { "securitySchemes": {
            "apiKeyAuth": { "type": "apiKey", "name": "api_key", "in": "query" } } },
          "paths": { "/a": { "get": { "summary": "A", "parameters": [
            { "name": "api_key", "in": "query", "required": true, "schema": { "type": "string" } } ] } } }
        }
        """);

        Assert.False(plan.Requests.Single().Request.QueryParams.Single(q => q.Key == "api_key").Enabled);
    }

    [Fact]
    public async Task A_request_with_no_auth_keeps_its_declared_Authorization_header_enabled()
    {
        // Nothing would suppress, so nothing is disabled: the spec asked for this header and the user
        // has no other way to send one.
        var plan = await PlanAsync("""
        {
          "openapi": "3.0.3",
          "info": { "title": "S", "version": "1" },
          "servers": [ { "url": "https://x" } ],
          "paths": { "/a": { "get": { "summary": "A", "parameters": [
            { "name": "Authorization", "in": "header", "required": true, "schema": { "type": "string" } } ] } } }
        }
        """);

        Assert.True(plan.Requests.Single().Request.Headers.Single(h => h.Key == "Authorization").Enabled);
    }

    private static string Url(Core.Import.OpenApiImportPlan plan, string requestName) =>
        plan.Requests.Single(r => r.Request.Name == requestName).Request.Url;
}
