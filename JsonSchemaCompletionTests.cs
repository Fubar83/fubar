using System.Linq;
using System.Text.Json.Nodes;
using Fubar.Controls;

namespace Fubar.Controls.Tests;

/// <summary>Unit tests for the JSON body schema completion engine (pure text + schema navigation).</summary>
public class JsonSchemaCompletionTests
{
    private const string Schema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["name"],
      "properties": {
        "name": { "type": "string", "description": "The pet name" },
        "age": { "type": "integer" },
        "status": { "type": "string", "enum": ["available", "pending", "sold"] },
        "vaccinated": { "type": "boolean" },
        "category": { "$ref": "#/$defs/Category" }
      },
      "$defs": {
        "Category": { "type": "object", "properties": { "id": { "type": "integer" }, "label": { "type": "string" } } }
      }
    }
    """;

    private static JsonNode Root => JsonNode.Parse(Schema)!;

    private static CompletionResult? At(string text)
    {
        var caret = text.IndexOf('|');
        return JsonSchemaCompletion.Compute(text.Replace("|", ""), caret, Root);
    }

    [Fact]
    public void SuggestsPropertyNames_AtKeyPosition()
    {
        var result = At("{ \"|");
        Assert.NotNull(result);
        var names = result!.Candidates.Select(c => c.FilterText).ToList();
        Assert.Contains("name", names);
        Assert.Contains("age", names);
        Assert.Contains("status", names);
    }

    [Fact]
    public void ExcludesPropertiesAlreadyPresent()
    {
        var result = At("{ \"name\": \"Rex\", \"|");
        Assert.NotNull(result);
        var names = result!.Candidates.Select(c => c.FilterText).ToList();
        Assert.DoesNotContain("name", names);
        Assert.Contains("age", names);
    }

    [Fact]
    public void KeyCandidate_InsertsQuotedNameAndColon()
    {
        var result = At("{ \"|");
        var name = result!.Candidates.Single(c => c.FilterText == "name");
        Assert.Equal("name\": ", name.InsertText);     // opening quote already typed
        Assert.Contains("required", name.Description);  // required flagged
        Assert.Contains("The pet name", name.Description);
    }

    [Fact]
    public void SuggestsEnumValues_AtValuePosition()
    {
        var result = At("{ \"status\": \"|");
        Assert.NotNull(result);
        Assert.Equal(new[] { "available", "pending", "sold" }, result!.Candidates.Select(c => c.FilterText));
        Assert.Equal("available\"", result.Candidates[0].InsertText); // closes its quote
    }

    [Fact]
    public void SuggestsNestedObjectProperties_ThroughRef()
    {
        var result = At("{ \"category\": { \"|");
        Assert.NotNull(result);
        var names = result!.Candidates.Select(c => c.FilterText).ToList();
        Assert.Contains("id", names);
        Assert.Contains("label", names);
    }

    [Fact]
    public void NoSuggestions_WhenNothingUseful()
    {
        // Inside a value string for a free-form field -> nothing to offer.
        Assert.Null(At("{ \"name\": \"|"));
    }
}
