namespace Fubar.Studio.Core.Import;

/// <summary>
/// Reads an OpenAPI 3.x / Swagger 2.0 spec (JSON or YAML, from a local file or an http(s) URL) and
/// materialises it into a workspace: a <c>collections/</c> subfolder of requests (one per operation,
/// grouped by tag), plus the environments, auth profiles and variables it can infer from the spec's
/// servers and security schemes. Parsing is separated from applying so the UI can preview what an
/// import would create (and pick options) before committing.
/// </summary>
public interface IOpenApiImportService
{
    /// <summary>
    /// Reads and parses the spec at <paramref name="source"/> (a file path or an http(s) URL) into an
    /// in-memory <see cref="OpenApiImportPlan"/> without writing anything. Throws
    /// <see cref="System.IO.InvalidDataException"/> for content that isn't valid OpenAPI/Swagger.
    /// </summary>
    Task<OpenApiImportPlan> ParseAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>Writes a parsed <paramref name="plan"/> into the workspace at
    /// <paramref name="workspaceRoot"/>, honouring <paramref name="options"/>. Returns a summary.</summary>
    Task<OpenApiImportResult> ApplyAsync(OpenApiImportPlan plan, string workspaceRoot, OpenApiImportOptions options, CancellationToken cancellationToken = default);

    /// <summary>Compares <paramref name="plan"/> against the current workspace and returns a per-item diff
    /// (add / update / unchanged / remove) for requests and environment variables, so the user can choose
    /// what to apply.</summary>
    Task<OpenApiImportDiff> DiffAsync(OpenApiImportPlan plan, string workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>Applies only the chosen <paramref name="selectedRequests"/> and
    /// <paramref name="selectedVariables"/> from a diff (creating / overwriting / deleting as each item's
    /// <see cref="ImportChange"/> dictates), leaving everything else - including the user's manual edits -
    /// untouched. <paramref name="plan"/> supplies the environment ids and auth profiles.</summary>
    Task<OpenApiImportResult> ApplyDiffAsync(
        OpenApiImportPlan plan,
        IReadOnlyCollection<RequestDiff> selectedRequests,
        IReadOnlyCollection<VariableDiff> selectedVariables,
        OpenApiImportOptions options,
        string workspaceRoot,
        CancellationToken cancellationToken = default);

    /// <summary>Convenience: <see cref="ParseAsync"/> then <see cref="ApplyAsync"/> with default options.</summary>
    Task<OpenApiImportResult> ImportAsync(string source, string workspaceRoot, CancellationToken cancellationToken = default);
}
