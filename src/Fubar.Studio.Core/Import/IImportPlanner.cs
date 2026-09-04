namespace Fubar.Studio.Core.Import;

/// <summary>
/// Turns some source document into an <see cref="ImportPlan"/> - the format-specific half of an import.
///
/// <para>An OpenAPI spec and a Postman collection differ only in how they are READ; everything after
/// that - diffing against the workspace, letting the user tick what to apply, writing the chosen items
/// - is the same work. Separating the two is what lets the Postman import show the same preview the
/// OpenAPI one always had, instead of writing straight into the workspace and reporting into a log.</para>
/// </summary>
public interface IImportPlanner
{
    /// <summary>What this reads, for the dialog's title and its file picker - "OpenAPI / Swagger",
    /// "Postman collection".</summary>
    string SourceDescription { get; }

    /// <summary>True when the source may be an http(s) URL as well as a file, so the dialog knows
    /// whether to offer a text box or only a Browse button.</summary>
    bool AcceptsUrl { get; }

    /// <summary>
    /// Reads <paramref name="source"/> into a plan, writing nothing. Throws
    /// <see cref="System.IO.InvalidDataException"/> for content this planner does not recognise.
    /// </summary>
    Task<ImportPlan> ParseAsync(string source, CancellationToken cancellationToken = default);
}

/// <summary>
/// The format-independent half: compare a plan against the workspace, then apply what was chosen.
///
/// <para>Split out of <see cref="IOpenApiImportService"/>, which is where it was implemented and where
/// it still lives - the code is unchanged, it simply no longer looks like it belongs to one format.</para>
/// </summary>
public interface IImportApplyService
{
    /// <summary>Compares <paramref name="plan"/> against the current workspace and returns a per-item
    /// diff (add / update / unchanged / remove) for requests and environment variables.</summary>
    Task<ImportDiff> DiffAsync(ImportPlan plan, string workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>Applies only the chosen items, leaving everything else - including the user's manual
    /// edits - untouched.</summary>
    Task<ImportResult> ApplyDiffAsync(
        ImportPlan plan,
        IReadOnlyCollection<RequestDiff> selectedRequests,
        IReadOnlyCollection<VariableDiff> selectedVariables,
        ImportOptions options,
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
