using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;
using Json.Schema;

namespace Fubar.Studio.Infrastructure.Workspaces;

/// <summary>One thing wrong with one file.</summary>
/// <param name="Path">The file, so a build log can be clicked.</param>
/// <param name="Location">JSON Pointer to the offending value, or "" for the document itself.</param>
/// <param name="Message">What is wrong, in the user's terms.</param>
/// <param name="IsError">False for a warning: real but not a reason to fail a build on its own.</param>
public sealed record ValidationProblem(string Path, string Location, string Message, bool IsError = true);

/// <summary>
/// Validates a workspace's files against the committed JSON Schemas, plus the checks a schema cannot
/// express.
///
/// <para>Schemas are EMBEDDED rather than fetched: <c>--validate</c> has to work on a build agent with
/// no network, and a validator that silently passes because it could not download its own schema is
/// worse than none. The published copies under <c>schemas/v1/</c> are the same files, and exist so an
/// editor can offer autocomplete from a <c>$schema</c> key.</para>
/// </summary>
public sealed class WorkspaceValidator
{
    private static readonly Dictionary<string, JsonSchema> Schemas = LoadEmbeddedSchemas();

    public IReadOnlyList<ValidationProblem> Validate(string workspaceRoot)
    {
        var problems = new List<ValidationProblem>();

        foreach (var (path, kind) in WorkspaceFiles.Enumerate(workspaceRoot))
        {
            problems.AddRange(ValidateFile(path, kind));
        }

        return problems;
    }

    public IReadOnlyList<ValidationProblem> ValidateFile(string path, WorkspaceFileKind kind)
    {
        JsonNode? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            // Malformed JSON is the one failure worth reporting on its own terms: no schema result
            // would say anything more useful than the parser already has.
            return [new ValidationProblem(path, "", $"Not valid JSON: {ex.Message}")];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [new ValidationProblem(path, "", $"Could not be read: {ex.Message}")];
        }

        if (document is null)
        {
            return [new ValidationProblem(path, "", "Empty document.")];
        }

        var problems = new List<ValidationProblem>();

        if (WorkspaceFiles.SchemaFor(kind) is { } schemaName && Schemas.TryGetValue(schemaName, out var schema))
        {
            // Through a JsonElement, which is the overload this version takes.
            using var element = JsonDocument.Parse(document.ToJsonString());
            var result = schema.Evaluate(element.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

            // The top-level verdict is checked FIRST, and nothing is reported when it passes.
            //
            // Without that guard a valid document produces errors: an if/then branch that did not
            // apply - "kind is secret, so value must be null" against a normal variable - is recorded
            // as a failed subschema even though the allOf around it succeeded. Walking every detail
            // reported four errors for a perfectly good environment file, which would have failed a
            // build over nothing. A document either conforms or it does not; only when it does not is
            // there anything worth saying about which part.
            if (!result.IsValid)
            {
                foreach (var detail in Flatten(result).Where(d => d is { IsValid: false, Errors.Count: > 0 }))
                {
                    foreach (var error in detail.Errors!)
                    {
                        problems.Add(new ValidationProblem(path, detail.InstanceLocation.ToString(), error.Value));
                    }
                }
            }
        }

        problems.AddRange(CredentialWarnings(path, kind, document));

        return problems;
    }

    /// <summary>
    /// The check no schema can express: a value that looks like a credential sitting in a file that
    /// gets committed.
    ///
    /// <para>A warning rather than an error, and deliberately: a deliberately public sandbox key is
    /// legitimate, and a validator that failed the build over one would be wrong about the case the
    /// user understands better than it does. Failing on warnings is <c>--strict</c>'s job.</para>
    /// </summary>
    private static IEnumerable<ValidationProblem> CredentialWarnings(string path, WorkspaceFileKind kind, JsonNode document)
    {
        if (kind is not (WorkspaceFileKind.Environment or WorkspaceFileKind.Manifest))
        {
            yield break;
        }

        var variables = document["variables"] as JsonArray;

        foreach (var variable in (variables ?? []).OfType<JsonObject>())
        {
            var key = variable["key"]?.GetValue<string>();
            var kindText = variable["kind"]?.GetValue<string>() ?? "normal";

            if (!string.Equals(kindText, "normal", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (variable["value"] is null || !CredentialNameHeuristic.LooksLikeCredential(key))
            {
                continue;
            }

            var value = variable["value"]!.ToString();
            if (value.Length == 0)
            {
                continue;
            }

            yield return new ValidationProblem(
                path,
                $"/variables/{variables!.IndexOf(variable)}/value",
                $"\"{key}\" looks like a credential and has a value in a committed file. "
                + "Mark it Secret to keep it in the OS keyring, or Session to keep it in memory.",
                IsError: false);
        }
    }

    /// <summary>
    /// JsonSchema.Net's list output is a tree; the leaves carry the messages.
    ///
    /// <para><c>Details</c> is null rather than empty for a leaf - the existing JsonSchemaValidator
    /// already guards for this, and skipping the guard here threw "Value cannot be null" out of the
    /// whole command on the first real workspace it met.</para>
    /// </summary>
    private static IEnumerable<EvaluationResults> Flatten(EvaluationResults results)
    {
        yield return results;

        foreach (var child in (results.Details ?? []).SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static Dictionary<string, JsonSchema> LoadEmbeddedSchemas()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".schema.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var name = resource[(resource.LastIndexOf("schemas.", StringComparison.Ordinal) + "schemas.".Length)..];

            using var reader = new StreamReader(stream);
            schemas[name] = JsonSchema.FromText(reader.ReadToEnd());
        }

        // Cross-file $refs (folder.schema.json points at request.schema.json) resolve through the
        // registry by $id, so every schema has to be registered before any is evaluated.
        foreach (var schema in schemas.Values)
        {
            SchemaRegistry.Global.Register(schema);
        }

        return schemas;
    }
}
