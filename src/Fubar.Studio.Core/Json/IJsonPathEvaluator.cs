namespace Fubar.Studio.Core.Json;

/// <summary>A single JSONPath match: the location it was found at and its value as compact JSON.</summary>
public sealed record JsonPathMatch(string Path, string Value);

/// <summary>The outcome of a JSONPath query. <see cref="Error"/> is set for invalid syntax or an
/// evaluation failure; an empty <see cref="Matches"/> with a null <see cref="Error"/> means "no matches".</summary>
public sealed record JsonPathQueryResult(IReadOnlyList<JsonPathMatch> Matches, string? Error);

/// <summary>
/// Evaluates a JSONPath expression against a JSON body. A domain port so the response viewer's live
/// filter depends on this abstraction rather than a concrete JSONPath library in the UI project.
/// </summary>
public interface IJsonPathEvaluator
{
    JsonPathQueryResult Evaluate(string bodyJson, string expression);
}
