using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Import;

/// <summary>How a planned item compares to what's already in the workspace.</summary>
public enum ImportChange
{
    /// <summary>Not in the workspace yet - will be created.</summary>
    Add,

    /// <summary>Exists and differs from the spec - can be overwritten with the spec's version.</summary>
    Update,

    /// <summary>Exists and already matches the spec - nothing to do.</summary>
    Unchanged,

    /// <summary>Exists (from a previous import) but is no longer in the spec - can be deleted.</summary>
    Remove,
}

/// <summary>One request's standing relative to the spec, for the import diff view.</summary>
public sealed record RequestDiff(
    ImportChange Change,
    string Method,
    string DisplayName,
    string FolderName,
    PlannedRequest? Planned,
    string? ExistingPath,
    string? ExistingId);

/// <summary>One environment variable's standing relative to the spec, for the import diff view.</summary>
public sealed record VariableDiff(
    ImportChange Change,
    string EnvironmentName,
    string Key,
    string? NewValue,
    string? ExistingValue,
    bool IsSecret);

/// <summary>
/// The result of comparing a parsed <see cref="OpenApiImportPlan"/> against the current workspace: what
/// the import would add / update / leave unchanged / remove, for requests and environment variables. The
/// UI presents this so the user can tick exactly which changes to apply (keeping manual edits), then
/// hands the chosen <see cref="RequestDiff"/>/<see cref="VariableDiff"/> items to
/// <see cref="IOpenApiImportService.ApplyDiffAsync"/>.
/// </summary>
public sealed record OpenApiImportDiff(
    string ApiTitle,
    string ApiFolderPath,
    IReadOnlyList<RequestDiff> Requests,
    IReadOnlyList<VariableDiff> Variables,
    IReadOnlyList<string> Warnings);
