namespace Fubar.Studio.Core.Variables;

/// <summary>
/// Outcome of resolving one <c>{{key}}</c> token - drives the Universal Variable Tooltip system's
/// valid (blue) vs undefined (amber) styling and hover tooltip (RequestEditorPane.md §4).
/// </summary>
public sealed record VariableResolution(bool IsDefined, string Value, string SourceName);
