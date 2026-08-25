namespace Fubar.Studio.Core.Import;

/// <summary>
/// Choices the user makes in the import preview before an <see cref="OpenApiImportPlan"/> is applied.
/// </summary>
public sealed record OpenApiImportOptions
{
    /// <summary>Existing collections folder to import into; null creates a new folder named after the
    /// API title directly under <c>collections/</c>.</summary>
    public string? TargetFolderPath { get; init; }

    /// <summary>Whether to create the inferred environment(s) (baseUrl + variables).</summary>
    public bool CreateEnvironments { get; init; } = true;

    /// <summary>Whether to create the inferred auth profiles (and wire requests to them).</summary>
    public bool CreateAuthProfiles { get; init; } = true;

    public static OpenApiImportOptions Default { get; } = new();
}
