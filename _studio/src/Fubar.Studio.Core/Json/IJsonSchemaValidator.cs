namespace Fubar.Studio.Core.Json;

/// <summary>
/// Validates a JSON document against a JSON Schema, returning human-readable problems
/// ("&lt;location&gt;: &lt;message&gt;"). A domain port so the presentation layer depends on this
/// abstraction rather than a concrete schema library in Infrastructure. Best-effort: an unparseable
/// schema or body yields no problems (never throws).
/// </summary>
public interface IJsonSchemaValidator
{
    IReadOnlyList<string> Validate(string schemaJson, string bodyJson);
}
