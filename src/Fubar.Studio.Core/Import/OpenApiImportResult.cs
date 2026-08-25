namespace Fubar.Studio.Core.Import;

/// <summary>
/// Summary of what an <see cref="IOpenApiImportService.ImportAsync"/> run materialised into a
/// workspace: the API title, the collections/ subfolder everything landed under, and counts of the
/// requests, folders, environments, auth profiles and variables created, plus any non-fatal warnings
/// (e.g. an unsupported security scheme that was skipped).
/// </summary>
public sealed record OpenApiImportResult(
    string ApiTitle,
    string TargetFolderPath,
    int RequestCount,
    int FolderCount,
    int EnvironmentCount,
    int AuthProfileCount,
    int VariableCount,
    IReadOnlyList<string> Warnings,
    int CreatedCount = 0,
    int UpdatedCount = 0);
