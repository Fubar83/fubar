using System.IO;
using System.Text.Json.Nodes;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Infrastructure.Import;

/// <summary>
/// Imports a Postman Collection v2.1 export. The folder/request tree is written under
/// <c>collections/&lt;collection name&gt;/</c>, and collection-level variables become an environment.
/// Postman's <c>{{variable}}</c> tokens match this app's, so URLs/headers/bodies carry over verbatim.
/// </summary>
public sealed class PostmanImporter : IPostmanImportService
{
    private readonly IWorkspaceService _workspaceService;

    public PostmanImporter(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public string SourceDescription => "Postman collection";

    /// <summary>A Postman export is a file you downloaded, not a URL you subscribe to.</summary>
    public bool AcceptsUrl => false;

    /// <summary>
    /// Reads a collection into the same <see cref="ImportPlan"/> an OpenAPI spec produces, writing
    /// nothing.
    ///
    /// <para>This is what lets a Postman import show the add / update / unchanged / remove preview the
    /// OpenAPI one always had. It wrote straight into the workspace before, reporting into a status
    /// log that was collapsed by default - so re-importing a collection silently overwrote whatever
    /// had been edited since the last time.</para>
    ///
    /// <para>An ENVIRONMENT export is not planned: it produces no requests, so there is nothing to
    /// preview. <see cref="ImportAsync"/> still handles those directly.</para>
    /// </summary>
    public async Task<ImportPlan> ParseAsync(string source, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(source, cancellationToken);
        if (JsonNode.Parse(json) is not JsonObject root)
        {
            throw new InvalidDataException("Not valid JSON.");
        }

        if (root["item"] is not JsonArray items)
        {
            throw new InvalidDataException(
                "Not a Postman collection (expected a top-level \"item\" array; v2.1 export). "
                + "An environment export has no requests to preview - import it directly.");
        }

        var collectionName = Str(root["info"]?["name"]) ?? Path.GetFileNameWithoutExtension(source);
        var warnings = new List<string>();
        var planned = new List<PlannedRequest>();

        // Postman nests folders arbitrarily; a plan carries one folder name per request. Nested paths
        // are joined with "/" so the structure survives rather than being flattened.
        void Walk(JsonArray nodes, string folder)
        {
            foreach (var node in nodes.OfType<JsonObject>())
            {
                if (node["item"] is JsonArray children)
                {
                    var name = Str(node["name"]) ?? "Folder";
                    Walk(children, folder.Length == 0 ? name : $"{folder}/{name}");
                }
                else if (node["request"] is not null)
                {
                    var model = BuildRequest(node, warnings);
                    ApplyScripts(node, model, warnings);
                    planned.Add(new PlannedRequest(folder, model));
                }
            }
        }

        Walk(items, "");

        var variables = ReadCollectionVariables(root["variable"] as JsonArray);
        var environments = variables.Count > 0
            ? new List<WorkspaceEnvironment> { new() { Name = $"{collectionName} (imported)", Variables = variables } }
            : [];

        return new ImportPlan(collectionName, planned, environments, [], warnings);
    }

    public async Task<PostmanImportResult> ImportAsync(string filePath, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (JsonNode.Parse(json) is not JsonObject root)
        {
            throw new InvalidDataException("Not valid JSON.");
        }

        // An ENVIRONMENT export, which is a separate file type teams have and this could not read at
        // all - so the variables a collection referenced arrived with nowhere to resolve from.
        // Recognised by its own marker rather than by the absence of "item", so a malformed collection
        // still gets the collection error rather than being silently treated as an environment.
        if (Str(root["_postman_variable_scope"]) is "environment" or "globals"
            || (root["values"] is JsonArray && root["item"] is null))
        {
            return await ImportEnvironmentAsync(root, filePath, workspaceRoot, cancellationToken);
        }

        if (root["item"] is not JsonArray items)
        {
            throw new InvalidDataException("Not a Postman collection (expected a top-level \"item\" array; v2.1 export).");
        }

        var collectionName = Str(root["info"]?["name"]) ?? Path.GetFileNameWithoutExtension(filePath);
        var warnings = new List<string>();
        var requestCount = 0;
        var folderCount = 0;

        var baseFolder = GetOrCreateFolder(Path.Combine(workspaceRoot, "collections"), collectionName);

        async Task WalkAsync(JsonArray nodes, string parentDir)
        {
            foreach (var node in nodes.OfType<JsonObject>())
            {
                if (node["item"] is JsonArray childItems)
                {
                    var folder = GetOrCreateFolder(parentDir, Str(node["name"]) ?? "Folder");
                    folderCount++;
                    await WalkAsync(childItems, folder);
                }
                else if (node["request"] is not null)
                {
                    var model = BuildRequest(node, warnings);
                    ApplyScripts(node, model, warnings);
                    var path = _workspaceService.CreateRequest(parentDir, model.Name);
                    await _workspaceService.SaveRequestAsync(path, model, cancellationToken);
                    requestCount++;
                }
            }
        }

        await WalkAsync(items, baseFolder);

        var variables = ReadCollectionVariables(root["variable"] as JsonArray);
        if (variables.Count > 0)
        {
            var environment = new WorkspaceEnvironment { Name = $"{collectionName} (imported)", Variables = variables };
            await _workspaceService.SaveEnvironmentAsync(workspaceRoot, environment, cancellationToken);
        }

        return new PostmanImportResult(collectionName, requestCount, folderCount, variables.Count, warnings);
    }

    /// <summary>
    /// Imports a Postman ENVIRONMENT export - a separate file type from a collection, and one this
    /// could not read at all, so the variables an imported collection referenced had nowhere to
    /// resolve from.
    ///
    /// <para>Postman's <c>secret</c> type becomes this app's <see cref="VariableKind.Secret"/>, and its
    /// value is deliberately NOT carried across: a Secret variable's value lives in the OS keyring, and
    /// writing it into <c>environments/*.json</c> on the way in would be the leak the rest of this
    /// codebase is arranged to prevent - performed by the import itself. The user is told to re-enter
    /// them, which is the honest cost of not having them on disk.</para>
    /// </summary>
    private async Task<PostmanImportResult> ImportEnvironmentAsync(
        JsonObject root, string filePath, string workspaceRoot, CancellationToken cancellationToken)
    {
        var name = Str(root["name"]) ?? Path.GetFileNameWithoutExtension(filePath);
        var warnings = new List<string>();
        var variables = new List<AppVariable>();
        var secrets = 0;

        foreach (var value in (root["values"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            if (Str(value["key"]) is not { Length: > 0 } key)
            {
                continue;
            }

            // Postman's "enabled: false" is a variable the user switched off rather than deleted;
            // importing it as an ordinary one would silently turn it back on.
            if (value["enabled"] is JsonValue enabled && enabled.TryGetValue<bool>(out var on) && !on)
            {
                warnings.Add($"\"{key}\" was disabled in the export and was not imported.");
                continue;
            }

            var isSecret = string.Equals(Str(value["type"]), "secret", StringComparison.OrdinalIgnoreCase);
            if (isSecret)
            {
                secrets++;
            }

            variables.Add(new AppVariable
            {
                Key = key,
                Kind = isSecret ? VariableKind.Secret : VariableKind.Normal,
                Value = isSecret ? null : Str(value["value"]),
            });
        }

        if (secrets > 0)
        {
            warnings.Add(
                $"{secrets} secret variable(s) were imported without their values - a secret lives in the OS "
                + "keyring, never in the committed environment file. Re-enter them in the environment editor.");
        }

        var environment = new WorkspaceEnvironment { Name = name, Variables = variables };
        await _workspaceService.SaveEnvironmentAsync(workspaceRoot, environment, cancellationToken);

        return new PostmanImportResult(name, RequestCount: 0, FolderCount: 0, variables.Count, warnings);
    }

    /// <summary>
    /// Translates the item's <c>test</c> script into assertions and captures, and says what it could
    /// not translate.
    ///
    /// <para>The <c>event</c> array was never read at all, so every script a team had written was
    /// dropped in silence - and the assertions people wrote are the thing they most want to keep when
    /// leaving Postman. What cannot be translated is now named, per request and line by line, rather
    /// than disappearing.</para>
    /// </summary>
    private static void ApplyScripts(JsonObject item, RequestModel model, List<string> warnings)
    {
        foreach (var listener in (item["event"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var kind = Str(listener["listen"]);
            var lines = (listener["script"]?["exec"] as JsonArray)?.Select(Str).OfType<string>().ToList() ?? [];

            if (lines.Count == 0)
            {
                continue;
            }

            // A pre-request script runs BEFORE the send and can do anything - compute a signature, set
            // a header. There is nothing declarative here that corresponds, so it is reported whole
            // rather than half-translated into something that would run at the wrong time.
            if (!string.Equals(kind, "test", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"\"{model.Name}\": its {kind ?? "pre-request"} script was not imported "
                    + $"({lines.Count} line(s)) - this app has no scripting.");
                continue;
            }

            var translation = PostmanScriptTranslation.Translate(lines);

            model.Assertions.AddRange(translation.Assertions);
            model.Captures.AddRange(translation.Captures);

            if (translation.AnythingTranslated)
            {
                warnings.Add(
                    $"\"{model.Name}\": translated {translation.Assertions.Count} assertion(s) and "
                    + $"{translation.Captures.Count} capture(s) from its test script.");
            }

            foreach (var line in translation.Untranslated)
            {
                warnings.Add($"\"{model.Name}\": could not translate  {line}");
            }
        }
    }

    private static RequestModel BuildRequest(JsonObject item, List<string> warnings)
    {
        var request = item["request"]!.AsObject();
        var name = Str(item["name"]) ?? "Request";
        var method = (Str(request["method"]) ?? "GET").ToUpperInvariant();

        var (url, query) = ReadUrl(request["url"]);

        var model = new RequestModel
        {
            Name = name,
            Method = method,
            Url = url,
            QueryParams = query,
            Headers = ReadHeaders(request["header"] as JsonArray),
        };

        ReadBody(request["body"] as JsonObject, model, name, warnings);
        ReadAuth(request["auth"] as JsonObject, model);

        return model;
    }

    private static (string Url, List<KeyValueItem> Query) ReadUrl(JsonNode? url)
    {
        // url is either a plain string or an object { raw, query[] }.
        var raw = url switch
        {
            JsonValue => Str(url) ?? "",
            JsonObject obj => Str(obj["raw"]) ?? "",
            _ => "",
        };

        var query = new List<KeyValueItem>();
        if (url is JsonObject o && o["query"] is JsonArray q)
        {
            foreach (var entry in q.OfType<JsonObject>())
            {
                if (Str(entry["key"]) is { } key)
                {
                    query.Add(new KeyValueItem { Key = key, Value = Str(entry["value"]) ?? "", Enabled = !IsDisabled(entry) });
                }
            }
        }

        // Strip the query string from the stored URL when we captured params separately, so the editor's
        // URL/Params sync doesn't show them twice.
        if (query.Count > 0)
        {
            var qm = raw.IndexOf('?');
            if (qm >= 0)
            {
                raw = raw[..qm];
            }
        }

        return (raw, query);
    }

    private static List<KeyValueItem> ReadHeaders(JsonArray? headers)
    {
        var result = new List<KeyValueItem>();
        if (headers is null)
        {
            return result;
        }

        foreach (var h in headers.OfType<JsonObject>())
        {
            if (Str(h["key"]) is { } key)
            {
                result.Add(new KeyValueItem { Key = key, Value = Str(h["value"]) ?? "", Enabled = !IsDisabled(h) });
            }
        }

        return result;
    }

    private static void ReadBody(JsonObject? body, RequestModel model, string name, List<string> warnings)
    {
        if (body is null)
        {
            return;
        }

        switch (Str(body["mode"]))
        {
            case "raw":
                var language = Str(body["options"]?["raw"]?["language"]);
                var raw = Str(body["raw"]) ?? "";
                model.Body = new RequestBody
                {
                    Type = string.Equals(language, "json", StringComparison.OrdinalIgnoreCase) ? BodyType.Json : BodyType.RawText,
                    Raw = raw,
                };
                break;

            case "urlencoded":
                model.Body = new RequestBody { Type = BodyType.UrlEncoded, UrlEncoded = ReadKeyValues(body["urlencoded"] as JsonArray) };
                break;

            case "formdata":
                model.Body = new RequestBody { Type = BodyType.FormData, FormData = ReadKeyValues(body["formdata"] as JsonArray) };
                break;

            case "graphql":
                model.Body = new RequestBody { Type = BodyType.Json, Raw = Str(body["graphql"]?["query"]) ?? "" };
                warnings.Add($"\"{name}\": GraphQL body imported as its raw query text.");
                break;

            case "file":
                warnings.Add($"\"{name}\": file body skipped (not supported by import).");
                break;
        }
    }

    private static void ReadAuth(JsonObject? auth, RequestModel model)
    {
        if (auth is null)
        {
            return;
        }

        switch (Str(auth["type"]))
        {
            case "bearer":
                model.Auth = new AuthConfig { Type = AuthType.Bearer, Token = ReadAuthParam(auth["bearer"], "token") ?? "" };
                break;

            case "basic":
                model.Auth = new AuthConfig
                {
                    Type = AuthType.Basic,
                    Username = ReadAuthParam(auth["basic"], "username") ?? "",
                    Password = ReadAuthParam(auth["basic"], "password") ?? "",
                };
                break;

            case "apikey":
                var inField = ReadAuthParam(auth["apikey"], "in");
                model.Auth = new AuthConfig
                {
                    Type = AuthType.ApiKey,
                    ApiKeyName = ReadAuthParam(auth["apikey"], "key") ?? "",
                    ApiKeyValue = ReadAuthParam(auth["apikey"], "value") ?? "",
                    ApiKeyLocation = string.Equals(inField, "query", StringComparison.OrdinalIgnoreCase)
                        ? ApiKeyLocation.QueryParam
                        : ApiKeyLocation.Header,
                };
                break;
        }
    }

    /// <summary>Postman auth params are arrays of <c>{ key, value, type }</c>; this pulls the value for a
    /// given key (e.g. "token", "username").</summary>
    private static string? ReadAuthParam(JsonNode? array, string key) =>
        array is JsonArray arr
            ? arr.OfType<JsonObject>().FirstOrDefault(e => string.Equals(Str(e["key"]), key, StringComparison.OrdinalIgnoreCase)) is { } entry
                ? Str(entry["value"])
                : null
            : null;

    private static List<KeyValueItem> ReadKeyValues(JsonArray? array)
    {
        var result = new List<KeyValueItem>();
        if (array is null)
        {
            return result;
        }

        foreach (var e in array.OfType<JsonObject>())
        {
            if (Str(e["key"]) is { } key)
            {
                result.Add(new KeyValueItem { Key = key, Value = Str(e["value"]) ?? "", Enabled = !IsDisabled(e) });
            }
        }

        return result;
    }

    private static List<AppVariable> ReadCollectionVariables(JsonArray? variables)
    {
        var result = new List<AppVariable>();
        if (variables is null)
        {
            return result;
        }

        foreach (var v in variables.OfType<JsonObject>())
        {
            if (Str(v["key"]) is { } key && !IsDisabled(v))
            {
                result.Add(new AppVariable { Key = key, Value = Str(v["value"]) ?? "" });
            }
        }

        return result;
    }

    private static bool IsDisabled(JsonObject entry) =>
        entry["disabled"] is JsonValue d && d.TryGetValue<bool>(out var b) && b;

    private static string GetOrCreateFolder(string parent, string name)
    {
        var path = Path.Combine(parent, SanitizeFolderName(name));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizeFolderName(string name) =>
        Core.Workspaces.DocumentName.Sanitize(name, "Imported");

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
