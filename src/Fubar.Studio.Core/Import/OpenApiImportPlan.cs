using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Import;

/// <summary>One request the import will create, plus the tag subfolder (relative to the API folder,
/// empty for none) it belongs under.</summary>
public sealed record PlannedRequest(string FolderName, RequestModel Request);

/// <summary>
/// The parsed, in-memory result of reading an OpenAPI/Swagger spec, before anything is written to disk.
/// Lets the UI preview what an import would create (and choose options) and then apply it. Produced by
/// <see cref="IOpenApiImportService.ParseAsync"/>; consumed by <see cref="IOpenApiImportService.ApplyAsync"/>.
/// </summary>
public sealed record OpenApiImportPlan(
    string ApiTitle,
    IReadOnlyList<PlannedRequest> Requests,
    IReadOnlyList<WorkspaceEnvironment> Environments,
    IReadOnlyList<AuthProfile> AuthProfiles,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Distinct tag subfolders plus the top-level API folder.</summary>
    public int FolderCount => 1 + Requests
        .Select(r => r.FolderName)
        .Where(f => f.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
}
