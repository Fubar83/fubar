using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Json;
using YamlDotNet.Serialization;

namespace Fubar.Studio.Infrastructure.Import;

/// <summary>
/// Materialises an OpenAPI 3.x / Swagger 2.0 spec (JSON or YAML, from a file or an http(s) URL) into a
/// workspace. Parsing (<see cref="ParseAsync"/>) is split from applying (<see cref="ApplyAsync"/>) so the
/// UI can preview a plan and pick options first. Requests (one per operation, grouped into tag subfolders
/// under an API-titled folder) go through <see cref="IWorkspaceService"/>; servers become <c>baseUrl</c>
/// across one or more environments (all inferred variables live in environments, since variables resolve
/// strictly against the active environment); security schemes become auth profiles plus the (mostly
/// secret) variables they reference. Local <c>$ref</c>s (parameters, request bodies, schemas) and
/// <c>allOf</c> composition are resolved.
/// </summary>
public sealed partial class OpenApiImportService : IOpenApiImportService
{
    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private readonly IWorkspaceService _workspaceService;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenApiImportService(IWorkspaceService workspaceService, IHttpClientFactory httpClientFactory)
    {
        _workspaceService = workspaceService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OpenApiImportResult> ImportAsync(string source, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var plan = await ParseAsync(source, cancellationToken);
        return await ApplyAsync(plan, workspaceRoot, OpenApiImportOptions.Default, cancellationToken);
    }

    // --- parse -------------------------------------------------------------------------------------

    public async Task<OpenApiImportPlan> ParseAsync(string source, CancellationToken cancellationToken = default)
    {
        var text = await ReadSpecAsync(source, cancellationToken);
        var root = ParseToJsonObject(text);
        return BuildPlan(root);
    }

    private async Task<string> ReadSpecAsync(string source, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var client = _httpClientFactory.CreateClient();
            return await client.GetStringAsync(uri, cancellationToken);
        }

        return await File.ReadAllTextAsync(source, cancellationToken);
    }

    // Accepts JSON or YAML: try JSON first, then fall back to converting YAML to a JSON object graph.
    private static JsonObject ParseToJsonObject(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is JsonObject fromJson)
            {
                return fromJson;
            }
        }
        catch (JsonException)
        {
            // Not JSON - try YAML below.
        }

        try
        {
            var graph = new DeserializerBuilder().Build().Deserialize<object?>(new StringReader(text));
            var json = new SerializerBuilder().JsonCompatible().Build().Serialize(graph);
            if (JsonNode.Parse(json) is JsonObject fromYaml)
            {
                return fromYaml;
            }
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or JsonException)
        {
            throw new InvalidDataException($"Couldn't parse the spec as JSON or YAML: {ex.Message}", ex);
        }

        throw new InvalidDataException("The spec's root is not a JSON/YAML object.");
    }

    private static OpenApiImportPlan BuildPlan(JsonObject root)
    {
        var isV2 = root["swagger"] is not null;
        if (root["openapi"] is null && !isV2)
        {
            throw new InvalidDataException("Not an OpenAPI 3.x or Swagger 2.0 document (missing the \"openapi\"/\"swagger\" field).");
        }

        var warnings = new List<string>();
        var title = Str(root["info"]?["title"]) ?? "Imported API";
        var components = root["components"] as JsonObject;
        var securitySchemes = (isV2 ? root["securityDefinitions"] : components?["securitySchemes"]) as JsonObject;

        // Which schemes the document actually uses. A spec commonly declares several - or inherits a
        // shared components block - and references one; building a profile and a variable for each of the
        // others fills the environment with credentials for auth nobody asked for.
        var usedSchemes = ReferencedSchemes(root);
        var (profiles, authVars) = BuildAuthProfiles(securitySchemes, usedSchemes, warnings);
        var globalSecurity = ResolveSecurity(root["security"], profiles);

        var requests = new List<PlannedRequest>();

        if (root["paths"] is JsonObject paths)
        {
            foreach (var (rawPath, pathItemNode) in paths)
            {
                if (pathItemNode is not JsonObject pathItem)
                {
                    continue;
                }

                var pathLevelParams = pathItem["parameters"] as JsonArray;

                foreach (var method in HttpMethods)
                {
                    if (pathItem[method] is not JsonObject op)
                    {
                        continue;
                    }

                    var model = BuildRequest(method, rawPath, op, pathLevelParams, root, profiles, globalSecurity, warnings);
                    var tag = Str((op["tags"] as JsonArray)?.FirstOrDefault()) is { Length: > 0 } t ? t : "";
                    requests.Add(new PlannedRequest(tag, model));
                }
            }
        }

        // The only inferred variables are the auth-scheme credentials. Variables resolve ONLY against the
        // active environment, so each environment carries the full set.
        //
        // PATH PARAMETERS ARE DELIBERATELY NOT VARIABLES. They used to be, one workspace-wide variable per
        // distinct {name} in the spec, and that is wrong twice over. It is wrong at scale - a mid-sized
        // API turns into dozens of empty variables nobody asked for - and it is wrong in kind, because the
        // names COLLIDE: /users/{id}, /users/{id}/orders and /orders/{id} all resolved to one shared "id",
        // so filling it in for one request broke the others. A path parameter belongs to the one request
        // whose URL contains it, and this app has no request-scoped variables by design
        // (RequestModel.LocalVariables is retired), so it goes inline in that URL instead - as the spec's
        // own {name}, or as the example/default when the spec supplies one.
        var commonVars = new List<AppVariable>(authVars);

        var servers = isV2 ? SyntheticV2Servers(root) : root["servers"] as JsonArray;
        var environments = BuildEnvironments(servers, commonVars);

        return new OpenApiImportPlan(title, requests, environments, profiles.Values.ToList(), warnings);
    }

    // --- apply -------------------------------------------------------------------------------------

    public async Task<OpenApiImportResult> ApplyAsync(OpenApiImportPlan plan, string workspaceRoot, OpenApiImportOptions options, CancellationToken cancellationToken = default)
    {
        // Reuse an existing folder (by name) rather than suffixing "Title 2", so re-importing the same
        // spec targets the same place and updates it.
        var apiFolder = options.TargetFolderPath ?? GetOrCreateFolder(Path.Combine(workspaceRoot, "collections"), plan.ApiTitle);
        Directory.CreateDirectory(apiFolder);

        // Index requests already under this folder so a re-import updates them in place (matched by the
        // stable operation key we stamp into Settings, falling back to method+URL for older imports).
        var existing = await ScanExistingRequestsAsync(apiFolder, cancellationToken);

        var tagFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;

        foreach (var planned in plan.Requests)
        {
            var folder = apiFolder;
            if (planned.FolderName.Length > 0)
            {
                if (!tagFolders.TryGetValue(planned.FolderName, out var tagFolder))
                {
                    tagFolder = GetOrCreateFolder(apiFolder, planned.FolderName);
                    tagFolders[planned.FolderName] = tagFolder;
                }

                folder = tagFolder;
            }

            var request = planned.Request;
            if (!options.CreateAuthProfiles && request.Auth.Type == AuthType.Profile)
            {
                request.Auth = new AuthConfig(); // don't dangle an AuthProfileId we won't create
                request.AuthProfileId = null;
            }

            if (MatchExisting(request, existing) is { } match)
            {
                request.Id = match.Id; // keep the same id when updating
                await _workspaceService.SaveRequestAsync(match.Path, request, cancellationToken);
                updated++;
            }
            else
            {
                var requestPath = _workspaceService.CreateRequest(folder, request.Name);
                await _workspaceService.SaveRequestAsync(requestPath, request, cancellationToken);
                created++;
            }
        }

        var requestCount = created + updated;

        var environmentCount = 0;
        var variableCount = 0;
        if (options.CreateEnvironments && plan.Environments.Count > 0)
        {
            foreach (var environment in plan.Environments)
            {
                await _workspaceService.SaveEnvironmentAsync(workspaceRoot, environment, cancellationToken);
            }

            environmentCount = plan.Environments.Count;
            variableCount = plan.Environments[0].Variables.Count;

            var workspace = await _workspaceService.LoadWorkspaceAsync(workspaceRoot, cancellationToken);
            if (string.IsNullOrEmpty(workspace.Manifest.ActiveEnvironmentId))
            {
                workspace.Manifest.ActiveEnvironmentId = plan.Environments[0].Id;
                await _workspaceService.SaveAppManifestAsync(workspaceRoot, workspace.Manifest, cancellationToken);
            }
        }

        var authProfileCount = 0;
        if (options.CreateAuthProfiles && plan.AuthProfiles.Count > 0)
        {
            // Replace same-named profiles from a prior import of this spec rather than piling up duplicates.
            var importedNames = plan.AuthProfiles.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var profiles = (await _workspaceService.LoadAuthProfilesAsync(workspaceRoot, cancellationToken))
                .Where(p => !importedNames.Contains(p.Name))
                .ToList();
            profiles.AddRange(plan.AuthProfiles);
            await _workspaceService.SaveAuthProfilesAsync(workspaceRoot, profiles, cancellationToken);
            authProfileCount = plan.AuthProfiles.Count;
        }

        return new OpenApiImportResult(
            plan.ApiTitle, apiFolder, requestCount, 1 + tagFolders.Count, environmentCount, authProfileCount, variableCount, plan.Warnings,
            created, updated);
    }

    private readonly record struct ExistingRequest(string Path, string Id);

    private static string? OperationKey(RequestModel request) => Str(request.Settings?["fubarOpenApi"]?["operationKey"]);

    private async Task<(Dictionary<string, ExistingRequest> ByKey, Dictionary<string, ExistingRequest> ByMethodUrl)> ScanExistingRequestsAsync(string folder, CancellationToken cancellationToken)
    {
        var byKey = new Dictionary<string, ExistingRequest>(StringComparer.Ordinal);
        var byMethodUrl = new Dictionary<string, ExistingRequest>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return (byKey, byMethodUrl);
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), "_folder.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var model = await _workspaceService.LoadRequestAsync(file, cancellationToken);
                var entry = new ExistingRequest(file, model.Id);
                if (OperationKey(model) is { } key)
                {
                    byKey[key] = entry;
                }

                byMethodUrl[$"{model.Method} {model.Url}"] = entry;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                // Skip anything that isn't a readable request.json.
            }
        }

        return (byKey, byMethodUrl);
    }

    private static ExistingRequest? MatchExisting(RequestModel request, (Dictionary<string, ExistingRequest> ByKey, Dictionary<string, ExistingRequest> ByMethodUrl) existing)
    {
        if (OperationKey(request) is { } key && existing.ByKey.TryGetValue(key, out var byKey))
        {
            return byKey;
        }

        return existing.ByMethodUrl.TryGetValue($"{request.Method} {request.Url}", out var byMethodUrl) ? byMethodUrl : null;
    }

    private static string GetOrCreateFolder(string parent, string name)
    {
        var path = Path.Combine(parent, SanitizeFolderName(name));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Imported" : sanitized;
    }

    // --- diff --------------------------------------------------------------------------------------

    public async Task<OpenApiImportDiff> DiffAsync(OpenApiImportPlan plan, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var apiFolder = Path.Combine(workspaceRoot, "collections", SanitizeFolderName(plan.ApiTitle));
        var existing = await LoadExistingRequestsAsync(apiFolder, cancellationToken);
        var matchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var requestDiffs = new List<RequestDiff>();
        foreach (var planned in plan.Requests)
        {
            var match = MatchExistingModel(planned.Request, existing);
            if (match is not { } m)
            {
                requestDiffs.Add(new RequestDiff(ImportChange.Add, planned.Request.Method, planned.Request.Name, planned.FolderName, planned, null, null));
                continue;
            }

            matchedPaths.Add(m.Path);
            var change = RequestsEquivalent(planned.Request, m.Model) ? ImportChange.Unchanged : ImportChange.Update;
            requestDiffs.Add(new RequestDiff(change, planned.Request.Method, planned.Request.Name, planned.FolderName, planned, m.Path, m.Model.Id));
        }

        // Requests from a prior import of this spec that the spec no longer has.
        foreach (var (path, model) in existing)
        {
            if (OperationKey(model) is not null && !matchedPaths.Contains(path))
            {
                requestDiffs.Add(new RequestDiff(ImportChange.Remove, model.Method, model.Name, "", null, path, model.Id));
            }
        }

        // Environment variables.
        var existingEnvs = await _workspaceService.LoadEnvironmentsAsync(workspaceRoot, cancellationToken);
        var variableDiffs = new List<VariableDiff>();
        foreach (var env in plan.Environments)
        {
            var existingEnv = existingEnvs.FirstOrDefault(e => string.Equals(e.Name, env.Name, StringComparison.Ordinal));
            var existingVars = existingEnv?.Variables ?? [];

            foreach (var variable in env.Variables)
            {
                var existingVar = existingVars.FirstOrDefault(v => string.Equals(v.Key, variable.Key, StringComparison.Ordinal));
                var change = existingVar is null ? ImportChange.Add
                    : (existingVar.Value ?? "") == (variable.Value ?? "") && existingVar.Kind == variable.Kind ? ImportChange.Unchanged
                    : ImportChange.Update;
                variableDiffs.Add(new VariableDiff(change, env.Name, variable.Key, variable.Value, existingVar?.Value, variable.Kind == VariableKind.Secret));
            }

            if (existingEnv is not null)
            {
                foreach (var orphan in existingEnv.Variables.Where(v => env.Variables.All(pv => pv.Key != v.Key)))
                {
                    variableDiffs.Add(new VariableDiff(ImportChange.Remove, env.Name, orphan.Key, null, orphan.Value, orphan.Kind == VariableKind.Secret));
                }
            }
        }

        return new OpenApiImportDiff(plan.ApiTitle, apiFolder, requestDiffs, variableDiffs, plan.Warnings);
    }

    public async Task<OpenApiImportResult> ApplyDiffAsync(
        OpenApiImportPlan plan,
        IReadOnlyCollection<RequestDiff> selectedRequests,
        IReadOnlyCollection<VariableDiff> selectedVariables,
        OpenApiImportOptions options,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var apiFolder = Path.Combine(workspaceRoot, "collections", SanitizeFolderName(plan.ApiTitle));
        Directory.CreateDirectory(apiFolder);

        var tagFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0, removed = 0;

        foreach (var diff in selectedRequests)
        {
            switch (diff.Change)
            {
                case ImportChange.Add when diff.Planned is { } planned:
                {
                    var request = PrepareAuth(planned.Request, options);
                    var folder = ResolveFolder(apiFolder, diff.FolderName, tagFolders);
                    var path = _workspaceService.CreateRequest(folder, request.Name);
                    await _workspaceService.SaveRequestAsync(path, request, cancellationToken);
                    created++;
                    break;
                }

                case ImportChange.Update when diff is { Planned: { } planned, ExistingPath: { } existingPath }:
                {
                    var request = PrepareAuth(planned.Request, options);
                    request.Id = diff.ExistingId ?? request.Id;
                    await _workspaceService.SaveRequestAsync(existingPath, request, cancellationToken);
                    updated++;
                    break;
                }

                case ImportChange.Remove when diff.ExistingPath is { } existingPath:
                    _workspaceService.DeletePath(existingPath);
                    removed++;
                    break;
            }
        }

        var variableCount = await ApplySelectedVariablesAsync(plan, selectedVariables, workspaceRoot, cancellationToken);

        var authProfileCount = 0;
        if (options.CreateAuthProfiles && plan.AuthProfiles.Count > 0)
        {
            var importedNames = plan.AuthProfiles.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var profiles = (await _workspaceService.LoadAuthProfilesAsync(workspaceRoot, cancellationToken))
                .Where(p => !importedNames.Contains(p.Name))
                .ToList();
            profiles.AddRange(plan.AuthProfiles);
            await _workspaceService.SaveAuthProfilesAsync(workspaceRoot, profiles, cancellationToken);
            authProfileCount = plan.AuthProfiles.Count;
        }

        var environmentCount = selectedVariables.Select(v => v.EnvironmentName).Distinct(StringComparer.Ordinal).Count();
        return new OpenApiImportResult(
            plan.ApiTitle, apiFolder, created + updated, 1 + tagFolders.Count, environmentCount, authProfileCount, variableCount, plan.Warnings, created, updated + removed);
    }

    private async Task<int> ApplySelectedVariablesAsync(OpenApiImportPlan plan, IReadOnlyCollection<VariableDiff> selectedVariables, string workspaceRoot, CancellationToken cancellationToken)
    {
        if (selectedVariables.Count == 0)
        {
            return 0;
        }

        var existingEnvs = (await _workspaceService.LoadEnvironmentsAsync(workspaceRoot, cancellationToken)).ToList();
        var applied = 0;

        foreach (var group in selectedVariables.GroupBy(v => v.EnvironmentName, StringComparer.Ordinal))
        {
            var planEnv = plan.Environments.FirstOrDefault(e => string.Equals(e.Name, group.Key, StringComparison.Ordinal));
            var env = existingEnvs.FirstOrDefault(e => string.Equals(e.Name, group.Key, StringComparison.Ordinal))
                ?? new WorkspaceEnvironment { Id = planEnv?.Id ?? Guid.NewGuid().ToString("n"), Name = group.Key };

            foreach (var diff in group)
            {
                var index = env.Variables.FindIndex(v => string.Equals(v.Key, diff.Key, StringComparison.Ordinal));
                if (diff.Change == ImportChange.Remove)
                {
                    if (index >= 0)
                    {
                        env.Variables.RemoveAt(index);
                    }

                    continue;
                }

                var description = planEnv?.Variables.FirstOrDefault(v => v.Key == diff.Key)?.Description;
                var variable = new AppVariable { Key = diff.Key, Value = diff.NewValue ?? "", Kind = diff.IsSecret ? VariableKind.Secret : VariableKind.Normal, Description = description };
                if (index >= 0)
                {
                    env.Variables[index] = variable;
                }
                else
                {
                    env.Variables.Add(variable);
                }

                applied++;
            }

            await _workspaceService.SaveEnvironmentAsync(workspaceRoot, env, cancellationToken);
        }

        // Activate the first plan environment if none is chosen yet.
        if (plan.Environments.Count > 0)
        {
            var workspace = await _workspaceService.LoadWorkspaceAsync(workspaceRoot, cancellationToken);
            if (string.IsNullOrEmpty(workspace.Manifest.ActiveEnvironmentId))
            {
                var envs = await _workspaceService.LoadEnvironmentsAsync(workspaceRoot, cancellationToken);
                var first = envs.FirstOrDefault(e => e.Name == plan.Environments[0].Name);
                if (first is not null)
                {
                    workspace.Manifest.ActiveEnvironmentId = first.Id;
                    await _workspaceService.SaveAppManifestAsync(workspaceRoot, workspace.Manifest, cancellationToken);
                }
            }
        }

        return applied;
    }

    private static string ResolveFolder(string apiFolder, string folderName, Dictionary<string, string> tagFolders)
    {
        if (folderName.Length == 0)
        {
            return apiFolder;
        }

        if (!tagFolders.TryGetValue(folderName, out var folder))
        {
            folder = GetOrCreateFolder(apiFolder, folderName);
            tagFolders[folderName] = folder;
        }

        return folder;
    }

    private static RequestModel PrepareAuth(RequestModel request, OpenApiImportOptions options)
    {
        if (!options.CreateAuthProfiles && request.Auth.Type == AuthType.Profile)
        {
            request.Auth = new AuthConfig();
            request.AuthProfileId = null;
        }

        return request;
    }

    private async Task<List<(string Path, RequestModel Model)>> LoadExistingRequestsAsync(string folder, CancellationToken cancellationToken)
    {
        var result = new List<(string, RequestModel)>();
        if (!Directory.Exists(folder))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), "_folder.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                result.Add((file, await _workspaceService.LoadRequestAsync(file, cancellationToken)));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Skip anything that isn't a readable request.json.
            }
        }

        return result;
    }

    private static (string Path, RequestModel Model)? MatchExistingModel(RequestModel request, List<(string Path, RequestModel Model)> existing)
    {
        if (OperationKey(request) is { } key)
        {
            foreach (var candidate in existing)
            {
                if (OperationKey(candidate.Model) == key)
                {
                    return candidate;
                }
            }
        }

        var methodUrl = $"{request.Method} {request.Url}";
        foreach (var candidate in existing)
        {
            if ($"{candidate.Model.Method} {candidate.Model.Url}" == methodUrl)
            {
                return candidate;
            }
        }

        return null;
    }

    // Two requests are equivalent if they serialize identically ignoring the id and import metadata.
    private static bool RequestsEquivalent(RequestModel a, RequestModel b) => Normalize(a) == Normalize(b);

    private static string Normalize(RequestModel request)
    {
        var clone = JsonSerializer.Deserialize<RequestModel>(JsonSerializer.Serialize(request, FubarJson.Options), FubarJson.Options)!;
        clone.Id = "";
        clone.Settings = null;
        return JsonSerializer.Serialize(clone, FubarJson.Options);
    }

    // --- requests ----------------------------------------------------------------------------------

    private static RequestModel BuildRequest(
        string method,
        string rawPath,
        JsonObject op,
        JsonArray? pathLevelParams,
        JsonObject root,
        IReadOnlyDictionary<string, AuthProfile> profiles,
        SecurityChoice globalSecurity,
        List<string> warnings)
    {
        var name = Str(op["summary"]) ?? Str(op["operationId"]) ?? $"{method.ToUpperInvariant()} {rawPath}";

        // Resolved up front because the URL is built from them: a path parameter with an example or a
        // default becomes that value, so the imported request is runnable; one without keeps the spec's
        // own {name}, which says WHICH parameter it is - something "<string>" would throw away, leaving
        // /users/<string>/orders/<string>.
        var pathValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in Concat(pathLevelParams, op["parameters"] as JsonArray))
        {
            if (ResolveRef(raw, root) is JsonObject pp
                && Str(pp["in"]) == "path"
                && Str(pp["name"]) is { } ppName
                && ConcreteParamValue(pp, pp["schema"] as JsonObject ?? pp) is { } value)
            {
                pathValues[ppName] = value;
            }
        }

        var model = new RequestModel
        {
            Name = name,
            Method = method.ToUpperInvariant(),
            Url = "{{baseUrl}}" + PathParamRegex().Replace(
                rawPath,
                m => pathValues.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value),
            // Stable identity so a later re-import updates this request in place instead of duplicating.
            Settings = new JsonObject { ["fubarOpenApi"] = new JsonObject { ["operationKey"] = $"{method.ToUpperInvariant()} {rawPath}" } },
        };

        var formFields = new List<KeyValueItem>();
        var queryNames = new JsonArray();
        var headerNames = new JsonArray();

        foreach (var raw in Concat(pathLevelParams, op["parameters"] as JsonArray))
        {
            if (ResolveRef(raw, root) is not JsonObject p || Str(p["name"]) is not { } pName)
            {
                continue;
            }

            switch (Str(p["in"]))
            {
                case "query":
                    model.QueryParams.Add(ParamItem(pName, p));
                    queryNames.Add(pName);
                    break;
                case "header":
                    model.Headers.Add(ParamItem(pName, p));
                    headerNames.Add(pName);
                    break;
                case "path":
                    break; // already in the URL - never a variable, see ParseAsync
                case "body": // Swagger 2.0 body parameter carries the schema directly
                    model.Body = new RequestBody { Type = BodyType.Json, Raw = Pretty(BuildExample(p["schema"], root, 0)) };
                    AttachBodySchema(model, p["schema"], root);
                    break;
                case "formData": // Swagger 2.0 form field
                    formFields.Add(new KeyValueItem { Key = pName, Value = "", Enabled = true });
                    break;
                case "cookie":
                    warnings.Add($"{model.Method} {rawPath}: cookie parameter \"{pName}\" skipped (not supported).");
                    break;
            }
        }

        if (formFields.Count > 0 && model.Body.Type == BodyType.None)
        {
            model.Body = new RequestBody { Type = BodyType.UrlEncoded, UrlEncoded = formFields };
        }

        // Remember the operation's declared query/header parameter names so the editor can offer
        // schema-aware key suggestions when the user adds a new param/header row later.
        if (queryNames.Count > 0 || headerNames.Count > 0)
        {
            ((JsonObject)model.Settings!["fubarOpenApi"]!)["parameters"] = new JsonObject
            {
                ["query"] = queryNames,
                ["header"] = headerNames,
            };
        }

        BuildBody(ResolveRef(op["requestBody"], root) as JsonObject, root, model);
        ApplySecurity(op, profiles, globalSecurity, model);
        DisableParamsTheAuthWillSend(model, profiles, warnings);

        // Add an Accept header matching the operation's declared response media types (what Swagger UI
        // sends), unless the spec already declared an Accept header parameter of its own.
        if (!model.Headers.Any(h => h.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
            && PreferredAccept(op, root) is { } accept)
        {
            model.Headers.Add(new KeyValueItem
            {
                Key = "Accept",
                Value = accept,
                Enabled = true,
                Description = "response media type (from the spec)",
            });
        }

        // Give the imported request a ready-made smoke test (a status-code assertion) and stash the
        // success response's JSON schema so the Response pane can validate what comes back.
        var (successCode, successResponse) = PrimarySuccess(op, root);
        if (successCode is { } code)
        {
            model.Assertions.Add(new Assertion
            {
                Source = ResponseField.StatusCode,
                Operator = AssertionOperator.Equals,
                Expected = code.ToString(),
            });
        }

        if (successResponse?["content"] is JsonObject respContent && respContent["application/json"] is JsonObject respJson)
        {
            AttachResponseSchema(model, respJson["schema"], root);
        }

        return model;
    }

    /// <summary>The operation's primary success response: the lowest concrete 2xx status (else the
    /// <c>default</c> response), with its resolved object. Used for the auto status assertion and the
    /// stashed response schema.</summary>
    private static (int? Code, JsonObject? Response) PrimarySuccess(JsonObject op, JsonObject root)
    {
        if (op["responses"] is not JsonObject responses)
        {
            return (null, null);
        }

        var best = responses
            .Where(kv => kv.Key.Length == 3 && kv.Key[0] == '2' && int.TryParse(kv.Key, out _))
            .OrderBy(kv => int.Parse(kv.Key))
            .FirstOrDefault();

        if (best.Key is not null && ResolveRef(best.Value, root) is JsonObject resp)
        {
            return (int.Parse(best.Key), resp);
        }

        return responses["default"] is { } def && ResolveRef(def, root) is JsonObject defResp ? (null, defResp) : (null, null);
    }

    private static void AttachResponseSchema(RequestModel model, JsonNode? schemaNode, JsonObject root)
    {
        if (schemaNode is null || model.Settings?["fubarOpenApi"] is not JsonObject meta)
        {
            return;
        }

        if (BuildSelfContainedSchema(schemaNode, root) is { } schema)
        {
            meta["responseSchema"] = schema;
        }
    }

    /// <summary>The media type to request via <c>Accept</c>: the operation's declared response content
    /// types (OpenAPI v3) or <c>produces</c> (Swagger v2), preferring JSON. Null when none are declared.</summary>
    private static string? PreferredAccept(JsonObject op, JsonObject root)
    {
        var mediaTypes = new List<string>();

        if (op["responses"] is JsonObject responses)
        {
            foreach (var (status, node) in responses)
            {
                if ((status.StartsWith('2') || status == "default")
                    && ResolveRef(node, root) is JsonObject resp
                    && resp["content"] is JsonObject content)
                {
                    mediaTypes.AddRange(content.Select(kv => kv.Key));
                }
            }
        }

        if (mediaTypes.Count == 0 && ((op["produces"] as JsonArray) ?? (root["produces"] as JsonArray)) is { } produces)
        {
            mediaTypes.AddRange(produces.Select(Str).Where(s => s is not null).Select(s => s!));
        }

        if (mediaTypes.Count == 0)
        {
            return null;
        }

        return mediaTypes.FirstOrDefault(m => m.Contains("json", StringComparison.OrdinalIgnoreCase)) ?? mediaTypes[0];
    }

    private static void BuildBody(JsonObject? requestBody, JsonObject root, RequestModel model)
    {
        if (requestBody?["content"] is not JsonObject content)
        {
            return;
        }

        if (content["application/json"] is JsonObject json)
        {
            var example = json["example"] ?? BuildExample(json["schema"], root, 0);
            model.Body = new RequestBody { Type = BodyType.Json, Raw = Pretty(example) };
            AttachBodySchema(model, json["schema"], root);
        }
        else if (content["application/x-www-form-urlencoded"] is JsonObject form)
        {
            model.Body = new RequestBody { Type = BodyType.UrlEncoded, UrlEncoded = SchemaFields(form["schema"], root) };
        }
        else if (content["multipart/form-data"] is JsonObject multipart)
        {
            model.Body = new RequestBody { Type = BodyType.FormData, FormData = SchemaFields(multipart["schema"], root) };
        }
    }

    private static List<KeyValueItem> SchemaFields(JsonNode? schema, JsonObject root)
    {
        var resolved = ResolveRef(schema, root);
        return resolved?["properties"] is JsonObject props
            ? props.Select(kv => new KeyValueItem { Key = kv.Key, Value = "", Enabled = true }).ToList()
            : [];
    }

    /// <summary>
    /// Turns OFF any imported header/query row whose name is the one the request's auth profile will
    /// send.
    ///
    /// <para>Specs routinely declare <c>Authorization</c> as an ordinary header parameter as well as
    /// declaring a security scheme. Imported as an enabled row it carries a placeholder, and
    /// <c>AuthRequestMerge</c> - correctly - refuses to overwrite a header the request already carries
    /// enabled, so the placeholder went out and the real token never did. Silent 401s that look like the
    /// auth profile is broken.</para>
    ///
    /// <para>Disabled rather than dropped: the spec said the parameter exists and that is worth keeping,
    /// and a disabled row does not suppress the auth (the merge only skips ENABLED ones), so ticking it
    /// back on is a deliberate act with a visible consequence.</para>
    /// </summary>
    private static void DisableParamsTheAuthWillSend(
        RequestModel model, IReadOnlyDictionary<string, AuthProfile> profiles, List<string> warnings)
    {
        if (model.AuthProfileId is not { } profileId
            || profiles.Values.FirstOrDefault(p => p.Id == profileId) is not { } profile)
        {
            return;
        }

        var (headerName, queryName) = profile.Config.Type switch
        {
            AuthType.ApiKey when profile.Config.ApiKeyLocation == ApiKeyLocation.QueryParam
                => (null, profile.Config.ApiKeyName),
            AuthType.ApiKey => (profile.Config.ApiKeyName, (string?)null),
            AuthType.Bearer or AuthType.Basic or AuthType.OAuth2 => ("Authorization", (string?)null),
            _ => (null, (string?)null),
        };

        Disable(model.Headers, headerName, "header");
        Disable(model.QueryParams, queryName, "query parameter");

        void Disable(List<KeyValueItem> items, string? key, string what)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            foreach (var item in items.Where(i => i.Enabled && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                item.Enabled = false;
                warnings.Add(
                    $"{model.Method} {model.Url}: the spec declares \"{item.Key}\" as a {what}, which is also what the "
                    + $"\"{profile.Name}\" security scheme sends. Imported unchecked so it cannot suppress the token.");
            }
        }
    }

    /// <summary>
    /// Every security scheme the document REFERENCES, from the global <c>security</c> and from each
    /// operation's. Empty when the document references none anywhere - in which case the caller keeps all
    /// of them, since a spec that declares auth and never wires it up still needs something to turn on.
    /// </summary>
    private static HashSet<string> ReferencedSchemes(JsonObject root)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        Collect(root["security"]);

        if (root["paths"] is JsonObject paths)
        {
            foreach (var (_, pathItemNode) in paths)
            {
                if (pathItemNode is not JsonObject pathItem)
                {
                    continue;
                }

                foreach (var method in HttpMethods)
                {
                    if (pathItem[method] is JsonObject op)
                    {
                        Collect(op["security"]);
                    }
                }
            }
        }

        return referenced;

        void Collect(JsonNode? security)
        {
            if (security is not JsonArray requirements)
            {
                return;
            }

            foreach (var requirement in requirements)
            {
                if (requirement is JsonObject ro)
                {
                    foreach (var (schemeName, _) in ro)
                    {
                        referenced.Add(schemeName);
                    }
                }
            }
        }
    }

    private static void ApplySecurity(JsonObject op, IReadOnlyDictionary<string, AuthProfile> profiles, SecurityChoice global, RequestModel model)
    {
        var choice = op["security"] is not null ? ResolveSecurity(op["security"], profiles) : global;

        if (choice.None)
        {
            model.Auth = new AuthConfig { Type = AuthType.None };
        }
        else if (choice.ProfileId is not null)
        {
            model.Auth = new AuthConfig { Type = AuthType.Profile };
            model.AuthProfileId = choice.ProfileId;
        }
    }

    // --- auth --------------------------------------------------------------------------------------

    private readonly record struct SecurityChoice(string? ProfileId, bool None);

    private static (Dictionary<string, AuthProfile> Profiles, List<AppVariable> Variables) BuildAuthProfiles(
        JsonObject? schemes, HashSet<string> usedSchemes, List<string> warnings)
    {
        var profiles = new Dictionary<string, AuthProfile>(StringComparer.Ordinal);
        var variables = new List<AppVariable>();

        if (schemes is null)
        {
            return (profiles, variables);
        }

        // Nothing anywhere references a scheme: the spec declares auth without wiring it up, and importing
        // no auth at all would leave nothing to switch on. Keep them all, and say so.
        var keepAll = usedSchemes.Count == 0;
        if (keepAll && schemes.Count > 0)
        {
            warnings.Add(
                "No operation references a security scheme, so all of them were imported. Delete the auth "
                + "profiles and variables you do not need.");
        }

        foreach (var (name, node) in schemes)
        {
            if (node is not JsonObject scheme)
            {
                continue;
            }

            if (!keepAll && !usedSchemes.Contains(name))
            {
                continue; // declared but never referenced - a credential for auth nobody asked for
            }

            var config = new AuthConfig();
            var slug = Slug(name);

            switch (Str(scheme["type"]))
            {
                case "apiKey":
                    config.Type = AuthType.ApiKey;
                    config.ApiKeyName = Str(scheme["name"]) ?? name;
                    config.ApiKeyLocation = string.Equals(Str(scheme["in"]), "query", StringComparison.OrdinalIgnoreCase)
                        ? ApiKeyLocation.QueryParam
                        : ApiKeyLocation.Header;
                    config.ApiKeyValue = $"{{{{apiKey_{slug}}}}}";
                    variables.Add(Secret($"apiKey_{slug}", $"API key for the \"{name}\" security scheme"));
                    break;

                case "basic": // v2
                case "http" when string.Equals(Str(scheme["scheme"]), "basic", StringComparison.OrdinalIgnoreCase):
                    config.Type = AuthType.Basic;
                    config.Username = $"{{{{basicUsername_{slug}}}}}";
                    config.Password = $"{{{{basicPassword_{slug}}}}}";
                    variables.Add(new AppVariable { Key = $"basicUsername_{slug}", Value = "", Description = $"Basic auth username for \"{name}\"" });
                    variables.Add(Secret($"basicPassword_{slug}", $"Basic auth password for \"{name}\""));
                    break;

                case "http": // v3 bearer (or any other http scheme - treat as a bearer token)
                    config.Type = AuthType.Bearer;
                    config.Token = $"{{{{bearerToken_{slug}}}}}";
                    variables.Add(Secret($"bearerToken_{slug}", $"Bearer token for the \"{name}\" security scheme"));
                    break;

                case "oauth2":
                case "openIdConnect":
                    config.Type = AuthType.OAuth2;
                    config.AccessToken = $"{{{{oauth2Token_{slug}}}}}";
                    variables.Add(Secret($"oauth2Token_{slug}", $"OAuth2 access token for the \"{name}\" security scheme"));
                    break;

                default:
                    warnings.Add($"Security scheme \"{name}\" (type \"{Str(scheme["type"])}\") isn't supported and was skipped.");
                    continue;
            }

            // Deterministic id from the scheme name so re-importing the same spec produces the same
            // profile id - otherwise every request's AuthProfileId would change and look "modified".
            profiles[name] = new AuthProfile { Id = DeterministicId($"auth:{name}"), Name = name, Config = config };
        }

        return (profiles, variables);
    }

    private static string DeterministicId(string seed) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(seed))).ToString("n");

    private static SecurityChoice ResolveSecurity(JsonNode? security, IReadOnlyDictionary<string, AuthProfile> profiles)
    {
        if (security is not JsonArray requirements)
        {
            return new SecurityChoice(null, false); // unspecified -> inherit
        }

        if (requirements.Count == 0)
        {
            return new SecurityChoice(null, true); // "security": [] -> explicitly none
        }

        foreach (var requirement in requirements)
        {
            if (requirement is JsonObject ro)
            {
                foreach (var (schemeName, _) in ro)
                {
                    if (profiles.TryGetValue(schemeName, out var profile))
                    {
                        return new SecurityChoice(profile.Id, false);
                    }
                }
            }
        }

        return new SecurityChoice(null, false);
    }

    // --- servers -> environments -------------------------------------------------------------------

    private static JsonArray? SyntheticV2Servers(JsonObject root)
    {
        var host = Str(root["host"]);
        if (host is null)
        {
            return null;
        }

        var basePath = Str(root["basePath"]) ?? "";
        var schemes = (root["schemes"] as JsonArray)?.Select(Str).OfType<string>().ToList() ?? ["https"];

        var servers = new JsonArray();
        foreach (var scheme in schemes)
        {
            servers.Add(new JsonObject { ["url"] = $"{scheme}://{host}{basePath}" });
        }

        return servers;
    }

    private static List<WorkspaceEnvironment> BuildEnvironments(JsonArray? servers, List<AppVariable> commonVars)
    {
        if (servers is null || servers.Count == 0)
        {
            return [MakeEnvironment("Default", "", commonVars)];
        }

        var expanded = new List<(string Url, string? Description)>();
        foreach (var server in servers)
        {
            if (server is not JsonObject so)
            {
                continue;
            }

            var url = Str(so["url"]) ?? "";
            if (so["variables"] is JsonObject serverVars)
            {
                // Substituted into the URL, and NOT also added as variables. They used to be both, which
                // made them inert - baseUrl already held the resolved URL, so nothing referenced them and
                // setting "region" to eu changed nothing - and made them wrong across environments, since
                // one server's variables were copied into every environment, first-value-wins, including
                // ones whose URL is literal and has no such variable. Making them live instead would need
                // recursive resolution (baseUrl containing {{region}}), which VariableResolver.Substitute
                // deliberately does not do: it is a single pass. Edit baseUrl per environment.
                foreach (var (varName, varDef) in serverVars)
                {
                    url = url.Replace("{" + varName + "}", Str(varDef?["default"]) ?? "");
                }
            }

            expanded.Add((url, Str(so["description"])));
        }

        if (expanded.Count == 1)
        {
            return [MakeEnvironment(expanded[0].Description is { Length: > 0 } d ? d : "Default", expanded[0].Url, commonVars)];
        }

        var environments = new List<WorkspaceEnvironment>();
        for (var i = 0; i < expanded.Count; i++)
        {
            var (url, description) = expanded[i];
            var envName = description is { Length: > 0 } d ? d : (HostOf(url) ?? $"Server {i + 1}");
            environments.Add(MakeEnvironment(envName, url, commonVars));
        }

        return environments;
    }

    private static WorkspaceEnvironment MakeEnvironment(string name, string baseUrl, List<AppVariable> commonVars)
    {
        var variables = new List<AppVariable> { new() { Key = "baseUrl", Value = baseUrl, Description = "OpenAPI server URL" } };
        variables.AddRange(commonVars.Select(v => new AppVariable { Key = v.Key, Value = v.Value, Kind = v.Kind, Description = v.Description }));
        return new WorkspaceEnvironment { Name = name, Variables = variables };
    }

    // --- body schema (for the editor's schema validation) ------------------------------------------

    // Stashes a self-contained JSON Schema for the request body on the request, so the Body editor can
    // validate what the user types against the operation's schema.
    private static void AttachBodySchema(RequestModel model, JsonNode? schemaNode, JsonObject root)
    {
        if (schemaNode is null || model.Settings?["fubarOpenApi"] is not JsonObject meta)
        {
            return;
        }

        if (BuildSelfContainedSchema(schemaNode, root) is { } schema)
        {
            meta["bodySchema"] = schema;
        }
    }

    // Bundles the operation's body schema plus every component schema into one draft-2020-12 document,
    // rewriting "#/components/schemas/X" (v3) and "#/definitions/X" (v2) refs to local "#/$defs/X" so the
    // result resolves standalone.
    private static JsonObject? BuildSelfContainedSchema(JsonNode schemaNode, JsonObject root)
    {
        if (schemaNode.DeepClone() is not JsonObject schema)
        {
            return null;
        }

        RewriteSchemaRefs(schema);

        var defs = new JsonObject();
        foreach (var bag in new[] { (root["components"] as JsonObject)?["schemas"] as JsonObject, root["definitions"] as JsonObject })
        {
            if (bag is null)
            {
                continue;
            }

            foreach (var (name, definition) in bag)
            {
                if (definition?.DeepClone() is { } clone)
                {
                    RewriteSchemaRefs(clone);
                    defs[name] = clone;
                }
            }
        }

        if (defs.Count > 0)
        {
            schema["$defs"] = defs;
        }

        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        return schema;
    }

    private static void RewriteSchemaRefs(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["$ref"] is JsonValue rv && rv.TryGetValue<string>(out var reference))
                {
                    foreach (var prefix in new[] { "#/components/schemas/", "#/definitions/" })
                    {
                        if (reference.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            obj["$ref"] = "#/$defs/" + reference[prefix.Length..];
                            break;
                        }
                    }
                }

                foreach (var child in obj.ToList())
                {
                    RewriteSchemaRefs(child.Value);
                }

                break;

            case JsonArray array:
                foreach (var item in array.ToList())
                {
                    RewriteSchemaRefs(item);
                }

                break;
        }
    }

    // --- schema example synthesis ------------------------------------------------------------------

    private static JsonNode? BuildExample(JsonNode? schema, JsonObject root, int depth)
    {
        if (ResolveRef(schema, root) is not JsonObject s || depth > 6)
        {
            return null;
        }

        if (s["example"] is { } example)
        {
            return example.DeepClone();
        }

        if (s["default"] is { } def)
        {
            return def.DeepClone();
        }

        if (s["enum"] is JsonArray enumValues && enumValues.Count > 0)
        {
            return enumValues[0]?.DeepClone();
        }

        // allOf: merge the properties of every subschema (and this schema's own) into one object.
        if (s["allOf"] is JsonArray allOf)
        {
            var merged = new JsonObject();
            foreach (var sub in allOf)
            {
                if (BuildExample(sub, root, depth + 1) is JsonObject partial)
                {
                    foreach (var (key, value) in partial)
                    {
                        merged[key] = value?.DeepClone();
                    }
                }
            }

            foreach (var (key, value) in BuildExampleProperties(s, root, depth))
            {
                merged[key] = value?.DeepClone();
            }

            return merged;
        }

        // oneOf / anyOf: use the first alternative.
        if ((s["oneOf"] ?? s["anyOf"]) is JsonArray alternatives && alternatives.Count > 0)
        {
            return BuildExample(alternatives[0], root, depth + 1);
        }

        if (s["properties"] is JsonObject || string.Equals(Str(s["type"]), "object", StringComparison.Ordinal))
        {
            return BuildExampleProperties(s, root, depth);
        }

        if (string.Equals(Str(s["type"]), "array", StringComparison.Ordinal))
        {
            return new JsonArray(BuildExample(s["items"], root, depth + 1) ?? "");
        }

        return Str(s["type"]) switch
        {
            "integer" => JsonValue.Create(0),
            "number" => JsonValue.Create(0.0),
            "boolean" => JsonValue.Create(false),
            _ => JsonValue.Create(SampleForFormat(Str(s["format"]))),
        };
    }

    private static JsonObject BuildExampleProperties(JsonObject schema, JsonObject root, int depth)
    {
        var obj = new JsonObject();
        if (schema["properties"] is JsonObject props)
        {
            foreach (var (propName, propSchema) in props)
            {
                obj[propName] = BuildExample(propSchema, root, depth + 1);
            }
        }

        return obj;
    }

    private static string SampleForFormat(string? format) => format switch
    {
        "date-time" => "1970-01-01T00:00:00Z",
        "date" => "1970-01-01",
        "email" => "user@example.com",
        "uuid" => "00000000-0000-0000-0000-000000000000",
        "uri" => "https://example.com",
        _ => "string",
    };

    // Resolves a local JSON-pointer $ref ("#/...") one or more hops; returns the node unchanged if it
    // isn't a ref. Handles $refs to schemas, parameters, request bodies, etc. anywhere in the document.
    private static JsonNode? ResolveRef(JsonNode? node, JsonObject root, int hops = 0)
    {
        if (hops > 10 || node is not JsonObject o || Str(o["$ref"]) is not { } reference || !reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return node;
        }

        JsonNode? current = root;
        foreach (var rawSegment in reference[2..].Split('/'))
        {
            var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            current = current switch
            {
                JsonObject co => co[segment],
                JsonArray ca when int.TryParse(segment, out var i) && i >= 0 && i < ca.Count => ca[i],
                _ => null,
            };

            if (current is null)
            {
                return null;
            }
        }

        return ResolveRef(current, root, hops + 1);
    }

    // --- small helpers -----------------------------------------------------------------------------

    // Turns a query/header parameter into a helpful row: a placeholder value (example/default, else a
    // <type> hint the user replaces), a description that leads with required/optional plus the type and
    // any enum options, and Enabled = required so optional params arrive unchecked (like Postman).
    private static KeyValueItem ParamItem(string name, JsonObject param)
    {
        var schema = param["schema"] as JsonObject ?? param; // v3 nests type/etc under schema; v2 is flat
        var required = param["required"] is JsonValue rv && rv.TryGetValue<bool>(out var b) && b;
        var deprecated = param["deprecated"] is JsonValue dv && dv.TryGetValue<bool>(out var d) && d;

        return new KeyValueItem
        {
            Key = name,
            Value = ParamPlaceholder(param, schema),
            Description = ParamDescription(param, schema, required, deprecated),
            // Required params arrive checked; optional ones unchecked (like Postman). A deprecated
            // param is left unchecked regardless, so it isn't sent by default.
            Enabled = required && !deprecated,
        };
    }

    private static string ParamPlaceholder(JsonObject param, JsonObject schema)
    {
        if (ConcreteParamValue(param, schema) is { } concrete)
        {
            return concrete;
        }

        var kind = Str(schema["format"]) ?? Str(schema["type"]);
        return kind is null ? "" : $"<{kind}>";
    }

    /// <summary>
    /// A real value the spec supplies for a parameter - an example, a default, or the first enum member -
    /// or null when it supplies none.
    ///
    /// <para>Split out from <see cref="ParamPlaceholder"/> for path parameters, which need to tell "the
    /// spec gave me something usable" from "it did not": the first goes into the URL, and the second
    /// leaves the spec's own <c>{name}</c> there rather than a "&lt;string&gt;" that says nothing about
    /// which parameter it is.</para>
    /// </summary>
    private static string? ConcreteParamValue(JsonObject param, JsonObject schema)
    {
        if (Str(param["example"]) is { } ex)
        {
            return ex;
        }

        if (Str(schema["example"]) is { } schemaExample)
        {
            return schemaExample;
        }

        if (Str(schema["default"]) is { } def)
        {
            return def;
        }

        // A concrete enum value is a better starting point than a "<type>" hint.
        if (schema["enum"] is JsonArray enumValues && enumValues.Count > 0 && Str(enumValues[0]) is { } first)
        {
            return first;
        }

        return null;
    }

    private static string ParamDescription(JsonObject param, JsonObject schema, bool required, bool deprecated)
    {
        var parts = new List<string> { required ? "required" : "optional" };

        if (deprecated)
        {
            parts.Add("deprecated");
        }

        if (Str(schema["type"]) is { } type)
        {
            parts.Add(Str(schema["format"]) is { } format ? $"{type} ({format})" : type);
        }

        if (schema["enum"] is JsonArray enumValues && enumValues.Count > 0)
        {
            parts.Add("one of: " + string.Join(", ", enumValues.Select(Str)));
        }

        var meta = string.Join(" · ", parts);
        return Str(param["description"]) is { Length: > 0 } description ? $"{meta} — {description}" : meta;
    }

    private static IEnumerable<JsonNode?> Concat(JsonArray? a, JsonArray? b)
    {
        if (a is not null)
        {
            foreach (var n in a) yield return n;
        }

        if (b is not null)
        {
            foreach (var n in b) yield return n;
        }
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s
        : node is JsonValue nv ? nv.ToString()
        : null;

    private static string Pretty(JsonNode? node) => node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";

    private static AppVariable Secret(string key, string description) => new() { Key = key, Value = "", Kind = VariableKind.Secret, Description = description };

    private static string Slug(string name) => new(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static string? HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    [GeneratedRegex(@"\{([^}/]+)\}")]
    private static partial Regex PathParamRegex();
}
