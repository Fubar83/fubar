namespace Fubar.Studio.Core.Import;

/// <summary>Summary of a Postman collection import.</summary>
public sealed record PostmanImportResult(string CollectionName, int RequestCount, int FolderCount, int VariableCount, IReadOnlyList<string> Warnings);

/// <summary>
/// Imports a Postman Collection v2.1 JSON export into a workspace: its folder/request tree becomes
/// requests under <c>collections/</c>, and its collection variables become an environment. Postman's
/// <c>{{variable}}</c> syntax matches this app's, so tokens carry over unchanged.
/// </summary>
public interface IPostmanImportService
{
    Task<PostmanImportResult> ImportAsync(string filePath, string workspaceRoot, CancellationToken cancellationToken = default);
}
