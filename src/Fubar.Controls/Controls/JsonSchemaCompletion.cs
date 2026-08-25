using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Fubar.Controls;

/// <summary>One completion candidate for the JSON body editor.</summary>
/// <param name="FilterText">Text the completion list filters/matches on (the bare name or value).</param>
/// <param name="Display">Text shown in the list.</param>
/// <param name="InsertText">Text written into the document, replacing from <see cref="CompletionResult.StartOffset"/>.</param>
/// <param name="Description">Optional tooltip (type / required / description).</param>
internal sealed record CompletionCandidate(string FilterText, string Display, string InsertText, string? Description);

/// <summary>The completions to offer at a caret, plus the offset the list should filter from.</summary>
internal sealed record CompletionResult(int StartOffset, IReadOnlyList<CompletionCandidate> Candidates);

/// <summary>
/// Computes schema-aware completions for a JSON document at a caret: property names for the object the
/// caret is in (minus ones already present), and enum / boolean values for a property's value position.
/// Pure text + schema navigation (no JSON Schema evaluation library) so it stays in the app-agnostic
/// control. Returns null whenever the context is unclear or there's nothing useful to suggest, so the
/// editor only pops the list when it genuinely helps.
/// </summary>
internal static class JsonSchemaCompletion
{
    public static CompletionResult? Compute(string text, int caret, JsonNode schemaRoot)
    {
        var context = ScanContext(text, caret);
        if (context is null)
        {
            return null;
        }

        var objectSchema = NavigateToObject(schemaRoot, context.Path);
        if (objectSchema is null)
        {
            return null;
        }

        var candidates = context.AtValue
            ? ValueCandidates(objectSchema, context.ValueKey, context.Quoted, schemaRoot)
            : KeyCandidates(objectSchema, context.PresentKeys, context.Quoted, schemaRoot);

        return candidates is { Count: > 0 } ? new CompletionResult(context.StartOffset, candidates) : null;
    }

    // --- context scanning --------------------------------------------------------------------------

    private sealed class Frame
    {
        public bool IsObject;
        public string? OpenedByKey;   // property name this frame sits under (null = array element / root)
        public string? PendingKey;    // last key seen, awaiting a value
        public bool ExpectingValue;   // just past a ':' for PendingKey
        public HashSet<string> Keys = new();
    }

    private sealed record Context(IReadOnlyList<string?> Path, bool AtValue, string? ValueKey, HashSet<string> PresentKeys, int StartOffset, bool Quoted);

    private static Context? ScanContext(string text, int caret)
    {
        // Empty to start: the body's own outermost '{' opens the root object (mapped to the root schema).
        var stack = new List<Frame>();
        var inString = false;
        var escape = false;
        var stringStart = -1;
        var builder = new StringBuilder();

        for (var i = 0; i < caret && i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape) { escape = false; builder.Append(c); continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"')
                {
                    inString = false;
                    if (stack.Count > 0)
                    {
                        var top = stack[^1];
                        if (top.IsObject && !top.ExpectingValue)
                        {
                            top.PendingKey = builder.ToString();
                            top.Keys.Add(top.PendingKey);
                        }
                        else
                        {
                            top.ExpectingValue = false; // consumed a string value
                        }
                    }

                    continue;
                }

                builder.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    stringStart = i;
                    builder.Clear();
                    break;
                case '{':
                case '[':
                    string? openedBy = null;
                    if (stack.Count > 0 && stack[^1].IsObject)
                    {
                        openedBy = stack[^1].PendingKey;
                        stack[^1].ExpectingValue = false;
                    }

                    stack.Add(new Frame { IsObject = c == '{', OpenedByKey = openedBy });
                    break;
                case '}':
                case ']':
                    if (stack.Count > 0) { stack.RemoveAt(stack.Count - 1); }
                    if (stack.Count > 0 && stack[^1].IsObject) { stack[^1].ExpectingValue = false; }
                    break;
                case ':':
                    if (stack.Count > 0) { stack[^1].ExpectingValue = true; }
                    break;
                case ',':
                    if (stack.Count > 0) { stack[^1].ExpectingValue = false; stack[^1].PendingKey = null; }
                    break;
            }
        }

        if (stack.Count == 0)
        {
            return null;
        }

        var frame = stack[^1];
        var path = stack.Skip(1).Select(f => f.OpenedByKey).ToList();

        if (inString)
        {
            // Caret sits inside an unterminated string: completing a partial key or value.
            var atValue = !frame.IsObject || frame.ExpectingValue;
            return new Context(path, atValue, frame.PendingKey, frame.Keys, stringStart + 1, Quoted: true);
        }

        // Not in a string: Ctrl+Space at a key slot, or a bare value slot (booleans etc.).
        if (frame.IsObject)
        {
            return frame.ExpectingValue
                ? new Context(path, AtValue: true, frame.PendingKey, frame.Keys, caret, Quoted: false)
                : new Context(path, AtValue: false, null, frame.Keys, caret, Quoted: false);
        }

        return null; // inside an array, no bare-value help for now
    }

    // --- schema navigation -------------------------------------------------------------------------

    private static JsonObject? NavigateToObject(JsonNode schemaRoot, IReadOnlyList<string?> path)
    {
        var current = Resolve(schemaRoot, schemaRoot, 0);
        foreach (var step in path)
        {
            if (current is null)
            {
                return null;
            }

            current = step is null
                ? Resolve(current["items"], schemaRoot, 0)                 // array element
                : Resolve(PropertySchema(current, step, schemaRoot), schemaRoot, 0); // object property
        }

        return current;
    }

    private static IReadOnlyList<CompletionCandidate> KeyCandidates(JsonObject objectSchema, HashSet<string> present, bool quoted, JsonNode root)
    {
        var required = RequiredKeys(objectSchema, root);
        var result = new List<CompletionCandidate>();
        foreach (var (name, propNode) in Properties(objectSchema, root))
        {
            if (present.Contains(name))
            {
                continue;
            }

            var prop = Resolve(propNode, root, 0);
            var type = prop is null ? null : Str(prop["type"]);
            var description = Str(prop?["description"]);
            var detail = string.Join(" · ", new[] { required.Contains(name) ? "required" : null, type, description }.Where(s => !string.IsNullOrEmpty(s)));
            var insert = quoted ? $"{name}\": " : $"\"{name}\": ";
            result.Add(new CompletionCandidate(name, name, insert, detail.Length > 0 ? detail : null));
        }

        return result;
    }

    private static IReadOnlyList<CompletionCandidate> ValueCandidates(JsonObject objectSchema, string? key, bool quoted, JsonNode root)
    {
        if (key is null || Resolve(PropertySchema(objectSchema, key, root), root, 0) is not { } prop)
        {
            return [];
        }

        if (prop["enum"] is JsonArray enumValues)
        {
            return enumValues
                .Select(Str)
                .Where(v => v is not null)
                .Select(v => new CompletionCandidate(v!, v!, quoted ? $"{v}\"" : $"\"{v}\"", "enum value"))
                .ToList();
        }

        if (Str(prop["type"]) == "boolean")
        {
            // Value is unquoted; drop the leading quote the trigger may have inserted context for.
            return
            [
                new CompletionCandidate("true", "true", "true", "boolean"),
                new CompletionCandidate("false", "false", "false", "boolean"),
            ];
        }

        return [];
    }

    // Properties of a schema, merging allOf. Follows $ref.
    private static IEnumerable<KeyValuePair<string, JsonNode?>> Properties(JsonObject schema, JsonNode root)
    {
        if (schema["properties"] is JsonObject props)
        {
            foreach (var kv in props)
            {
                yield return kv;
            }
        }

        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var sub in allOf)
            {
                if (Resolve(sub, root, 0) is { } subSchema)
                {
                    foreach (var kv in Properties(subSchema, root))
                    {
                        yield return kv;
                    }
                }
            }
        }
    }

    private static HashSet<string> RequiredKeys(JsonObject schema, JsonNode root)
    {
        var required = new HashSet<string>();
        void Collect(JsonObject s)
        {
            if (s["required"] is JsonArray req)
            {
                foreach (var r in req)
                {
                    if (Str(r) is { } name) { required.Add(name); }
                }
            }

            if (s["allOf"] is JsonArray allOf)
            {
                foreach (var sub in allOf)
                {
                    if (Resolve(sub, root, 0) is { } subSchema) { Collect(subSchema); }
                }
            }
        }

        Collect(schema);
        return required;
    }

    private static JsonNode? PropertySchema(JsonObject schema, string key, JsonNode root) =>
        Properties(schema, root).FirstOrDefault(kv => kv.Key == key).Value;

    // Follows local JSON-pointer $refs (e.g. "#/$defs/Pet") against the schema document.
    private static JsonObject? Resolve(JsonNode? node, JsonNode root, int depth)
    {
        if (depth > 20 || node is not JsonObject obj)
        {
            return node as JsonObject;
        }

        if (Str(obj["$ref"]) is not { } reference || !reference.StartsWith("#/", System.StringComparison.Ordinal))
        {
            return obj;
        }

        JsonNode? current = root;
        foreach (var raw in reference[2..].Split('/'))
        {
            var segment = raw.Replace("~1", "/").Replace("~0", "~");
            current = current is JsonObject co ? co[segment] : null;
            if (current is null)
            {
                return null;
            }
        }

        return Resolve(current, root, depth + 1);
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
