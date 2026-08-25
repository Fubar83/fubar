using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Tests.Json;

public class JsonSchemaValidatorTests
{
    private const string Schema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["name"],
      "properties": {
        "name": { "type": "string" },
        "age": { "type": "integer" },
        "status": { "type": "string", "enum": ["active", "inactive"] }
      }
    }
    """;

    [Fact]
    public void Validate_ValidBody_NoProblems()
    {
        Assert.Empty(new JsonSchemaValidator().Validate(Schema, """{ "name": "Rex", "age": 3, "status": "active" }"""));
    }

    [Fact]
    public void Validate_MissingRequiredField_ReportsProblem()
    {
        Assert.NotEmpty(new JsonSchemaValidator().Validate(Schema, "{ }"));
    }

    [Fact]
    public void Validate_WrongType_ReportsProblem()
    {
        Assert.NotEmpty(new JsonSchemaValidator().Validate(Schema, """{ "name": 123 }"""));
    }

    [Fact]
    public void Validate_BadEnumValue_ReportsProblem()
    {
        Assert.NotEmpty(new JsonSchemaValidator().Validate(Schema, """{ "name": "Rex", "status": "unknown" }"""));
    }

    [Fact]
    public void Validate_MalformedBodyOrSchema_ReturnsEmpty_NeverThrows()
    {
        Assert.Empty(new JsonSchemaValidator().Validate(Schema, "{ not json"));
        Assert.Empty(new JsonSchemaValidator().Validate("{ not a schema", "{ }"));
    }
}
