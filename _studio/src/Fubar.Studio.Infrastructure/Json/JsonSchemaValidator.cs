using System.Text.Json;
using Fubar.Studio.Core.Json;
using Json.Schema;

namespace Fubar.Studio.Infrastructure.Json;

/// <summary>
/// Validates a JSON document against a JSON Schema and returns human-readable problems ("&lt;location&gt;:
/// &lt;message&gt;"), for the request Body editor's schema intelligence. Schemas are the self-contained
/// draft-2020-12 documents the OpenAPI importer stashes on a request (refs rewritten to <c>#/$defs</c>).
/// Best-effort: a schema or body that doesn't parse yields no problems (syntax validity is surfaced
/// separately), so this never throws at the caller. Adapter for the <see cref="IJsonSchemaValidator"/>
/// domain port.
/// </summary>
public sealed class JsonSchemaValidator : IJsonSchemaValidator
{
    public IReadOnlyList<string> Validate(string schemaJson, string bodyJson)
    {
        JsonSchema schema;
        JsonDocument document;
        try
        {
            schema = JsonSchema.FromText(schemaJson);
            document = JsonDocument.Parse(bodyJson);
        }
        catch (Exception)
        {
            return [];
        }

        using (document)
        {
            var results = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (results.IsValid)
            {
                return [];
            }

            var problems = new List<string>();
            foreach (var detail in results.Details ?? [])
            {
                if (detail.Errors is not { Count: > 0 } errors)
                {
                    continue;
                }

                var location = detail.InstanceLocation.ToString();
                var where = string.IsNullOrEmpty(location) ? "(body root)" : location;
                foreach (var (_, message) in errors)
                {
                    problems.Add($"{where}: {message}");
                }
            }

            return problems.Distinct().ToList();
        }
    }
}
