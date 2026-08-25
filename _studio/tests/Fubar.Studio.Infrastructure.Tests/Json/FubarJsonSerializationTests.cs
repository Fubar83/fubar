using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Tests.Json;

public class FubarJsonSerializationTests
{
    [Fact]
    public void AppManifest_RoundTrips_WithCamelCasePropertyNames()
    {
        var manifest = new AppManifest
        {
            Id = "ws-1",
            Name = "Demo Workspace",
            Variables =
            [
                new AppVariable { Key = "baseUrl", Value = "https://example.com", Kind = VariableKind.Normal, Description = "API base" },
                new AppVariable { Key = "apiKey", Value = null, Kind = VariableKind.Secret },
            ],
        };

        var json = JsonSerializer.Serialize(manifest, FubarJson.Options);

        Assert.Contains("\"baseUrl\"", json);
        Assert.Contains("\"kind\": \"secret\"", json);
        Assert.DoesNotContain("\"isSecret\"", json); // legacy flag is no longer written
        Assert.DoesNotContain("\"Name\"", json); // PascalCase must not leak through

        var roundTripped = JsonSerializer.Deserialize<AppManifest>(json, FubarJson.Options);

        Assert.NotNull(roundTripped);
        Assert.Equal(manifest.Id, roundTripped.Id);
        Assert.Equal(manifest.Name, roundTripped.Name);
        Assert.Equal(2, roundTripped.Variables.Count);
        Assert.Equal(VariableKind.Secret, roundTripped.Variables[1].Kind);
        Assert.Null(roundTripped.Variables[1].Value);
    }

    [Fact]
    public void RequestModel_RoundTrips_KindAsLowercaseString()
    {
        var request = new RequestModel { Name = "Get User", Kind = RequestKind.GraphQl, Method = "POST" };

        var json = JsonSerializer.Serialize(request, FubarJson.Options);

        Assert.Contains("\"kind\": \"graphQl\"", json);

        var roundTripped = JsonSerializer.Deserialize<RequestModel>(json, FubarJson.Options);

        Assert.NotNull(roundTripped);
        Assert.Equal(RequestKind.GraphQl, roundTripped.Kind);
        Assert.Equal("POST", roundTripped.Method);
    }

    [Fact]
    public void RequestModel_DefaultKind_IsHttp_WhenFieldOmittedFromJson()
    {
        // Simulates loading a request.json written before "kind" existed - it must default to Http.
        const string legacyJson = """{ "name": "Legacy Request", "method": "GET", "url": "" }""";

        var request = JsonSerializer.Deserialize<RequestModel>(legacyJson, FubarJson.Options);

        Assert.NotNull(request);
        Assert.Equal(RequestKind.Http, request.Kind);
    }
}
