namespace Fubar.Studio.UI.ViewModels;

/// <summary>One match from the Tree view's JSONPath filter box (ResponsePane.md §5, "Live
/// Filtering Mode") - e.g. evaluating <c>$.items[*].id</c> against the response body.</summary>
public sealed record JsonPathMatchViewModel(string Path, string Value);
