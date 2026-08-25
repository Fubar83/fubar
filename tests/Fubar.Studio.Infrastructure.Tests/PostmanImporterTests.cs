using System.IO;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Import;

namespace Fubar.Studio.Infrastructure.Tests;

public class PostmanImporterTests
{
    private const string Collection = """
    {
      "info": { "name": "Sample API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
      "item": [
        {
          "name": "Users",
          "item": [
            {
              "name": "Create user",
              "request": {
                "method": "POST",
                "header": [
                  { "key": "Content-Type", "value": "application/json" },
                  { "key": "X-Disabled", "value": "nope", "disabled": true }
                ],
                "url": {
                  "raw": "{{baseUrl}}/users?verbose=true",
                  "query": [ { "key": "verbose", "value": "true" } ]
                },
                "body": { "mode": "raw", "raw": "{\"name\":\"Ada\"}", "options": { "raw": { "language": "json" } } },
                "auth": { "type": "bearer", "bearer": [ { "key": "token", "value": "{{token}}" } ] }
              }
            }
          ]
        }
      ],
      "variable": [ { "key": "baseUrl", "value": "https://api.example.com" } ]
    }
    """;

    [Fact]
    public async Task Imports_requests_variables_and_maps_fields()
    {
        var recorder = new RecordingWorkspaceService();
        var sut = new PostmanImporter(recorder);
        var root = Path.Combine(Path.GetTempPath(), "fubar-postman-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "collection.json");
        await File.WriteAllTextAsync(file, Collection);

        try
        {
            var result = await sut.ImportAsync(file, root);

            Assert.Equal("Sample API", result.CollectionName);
            Assert.Equal(1, result.RequestCount);
            Assert.Equal(1, result.FolderCount);
            Assert.Equal(1, result.VariableCount);

            var request = Assert.Single(recorder.SavedRequests);
            Assert.Equal("POST", request.Method);
            Assert.Equal("{{baseUrl}}/users", request.Url);
            Assert.Contains(request.QueryParams, p => p.Key == "verbose" && p.Value == "true");
            Assert.Contains(request.Headers, h => h.Key == "Content-Type");
            Assert.Contains(request.Headers, h => h.Key == "X-Disabled" && !h.Enabled);
            Assert.Equal(BodyType.Json, request.Body.Type);
            Assert.Equal(AuthType.Bearer, request.Auth.Type);
            Assert.Equal("{{token}}", request.Auth.Token);

            var environment = Assert.Single(recorder.SavedEnvironments);
            Assert.Contains(environment.Variables, v => v.Key == "baseUrl" && v.Value == "https://api.example.com");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
