using System.Text.Json;
using System.Text.Json.Nodes;
using Fubar.Studio.Core.Json;
using Json.Path;

namespace Fubar.Studio.Infrastructure.Json;

/// <summary>Adapter for the <see cref="IJsonPathEvaluator"/> domain port over the JsonPath.Net library.</summary>
public sealed class JsonPathEvaluator : IJsonPathEvaluator
{
    public JsonPathQueryResult Evaluate(string bodyJson, string expression)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(bodyJson);
        }
        catch (JsonException)
        {
            return new JsonPathQueryResult([], "Response is not valid JSON.");
        }

        if (node is null)
        {
            return new JsonPathQueryResult([], null);
        }

        if (!JsonPath.TryParse(expression, out var path))
        {
            return new JsonPathQueryResult([], "Invalid JSONPath syntax.");
        }

        try
        {
            var matches = path.Evaluate(node).Matches
                .Select(m => new JsonPathMatch(m.Location?.ToString() ?? "$", m.Value?.ToJsonString() ?? "null"))
                .ToList();
            return new JsonPathQueryResult(matches, null);
        }
        catch (Exception ex)
        {
            return new JsonPathQueryResult([], $"Evaluation failed: {ex.Message}");
        }
    }
}
