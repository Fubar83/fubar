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

    public async Task<PostmanImportResult> ImportAsync(string filePath, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (JsonNode.Parse(json) is not JsonObject root || root["item"] is not JsonArray items)
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

    private static string SanitizeFolderName(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Imported" : cleaned;
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
