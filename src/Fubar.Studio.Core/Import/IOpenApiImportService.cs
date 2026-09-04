namespace Fubar.Studio.Core.Import;

/// <summary>
/// Reads an OpenAPI 3.x / Swagger 2.0 spec (JSON or YAML, from a local file or an http(s) URL) and
/// materialises it into a workspace: a <c>collections/</c> subfolder of requests (one per operation,
/// grouped by tag), plus the environments, auth profiles and variables it can infer from the spec's
/// servers and security schemes. Parsing is separated from applying so the UI can preview what an
/// import would create (and pick options) before committing.
/// </summary>
public interface IOpenApiImportService : IImportPlanner, IImportApplyService
{
    /// <summary>Writes a parsed <paramref name="plan"/> into the workspace at
    /// <paramref name="workspaceRoot"/>, honouring <paramref name="options"/>. Returns a summary.</summary>
    Task<ImportResult> ApplyAsync(ImportPlan plan, string workspaceRoot, ImportOptions options, CancellationToken cancellationToken = default);

    /// <summary>Convenience: <see cref="ParseAsync"/> then <see cref="ApplyAsync"/> with default options.</summary>
    Task<ImportResult> ImportAsync(string source, string workspaceRoot, CancellationToken cancellationToken = default);
}
