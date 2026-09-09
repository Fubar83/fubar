using System.Text.Json.Serialization;

namespace Fubar.Studio.Core.Comparison;

/// <summary>
/// A field that is allowed to move, and by how much.
/// </summary>
/// <remarks>
/// <para>The difference between a suite that catches regressions and one that ignores half the
/// payload. Ignoring a total because it drifts by a cent stops checking the total; a tolerance keeps
/// checking it and only forgives the cent.</para>
/// <para>Exactly one kind per entry. Two on the same rule would need an order between them, and the
/// honest answer to "is this within 0.01 OR matching this regex" is that nobody writing the file
/// meant to ask - <see cref="Kind"/> reports what a rule with none or several actually is.</para>
/// </remarks>
public sealed class Tolerance
{
    /// <summary>The field, as <c>JsonPathMatcher</c> understands it.</summary>
    public required string Path { get; set; }

    /// <summary>Both sides are numbers within this of each other.</summary>
    public double? Numeric { get; set; }

    /// <summary>Both sides are timestamps within this many seconds of each other.</summary>
    public double? WithinSeconds { get; set; }

    /// <summary>Both sides match this regular expression. The field still has to look right - this
    /// forgives a generated id, not a missing one.</summary>
    public string? Matches { get; set; }

    /// <summary>The array's length changed by no more than this percent of the larger side.</summary>
    public double? LengthWithinPercent { get; set; }

    /// <summary>Both sides are one of these values.</summary>
    public List<string>? OneOf { get; set; }

    [JsonIgnore]

    public ToleranceKind Kind
    {
        get
        {
            var kinds = new List<ToleranceKind>(1);
            if (Numeric is not null) { kinds.Add(ToleranceKind.Numeric); }
            if (WithinSeconds is not null) { kinds.Add(ToleranceKind.WithinSeconds); }
            if (Matches is { Length: > 0 }) { kinds.Add(ToleranceKind.Matches); }
            if (LengthWithinPercent is not null) { kinds.Add(ToleranceKind.LengthWithinPercent); }
            if (OneOf is { Count: > 0 }) { kinds.Add(ToleranceKind.OneOf); }

            return kinds.Count == 1 ? kinds[0] : ToleranceKind.None;
        }
    }

    public Tolerance Clone() => new()
    {
        Path = Path,
        Numeric = Numeric,
        WithinSeconds = WithinSeconds,
        Matches = Matches,
        LengthWithinPercent = LengthWithinPercent,
        OneOf = OneOf is null ? null : [.. OneOf],
    };
}

/// <summary>Which kind of allowance a <see cref="Tolerance"/> states.</summary>
public enum ToleranceKind
{
    /// <summary>None, or more than one - either way there is nothing to apply, and the rule is
    /// reported rather than guessed at.</summary>
    None,

    Numeric,
    WithinSeconds,
    Matches,
    LengthWithinPercent,
    OneOf,
}

/// <summary>One tolerance in force, plus the level that set it.</summary>
public readonly record struct ResolvedTolerance(Tolerance Tolerance, ComparisonScope Scope, string SourceName);

/// <summary>One level's tolerances, and how to describe that level.</summary>
public sealed record ToleranceLayer(IReadOnlyList<Tolerance>? Tolerances, ComparisonScope Scope, string SourceName);

/// <summary>
/// Folds the chain into the tolerances that apply. Per PATH, closest wins (spec §4.4): a level
/// restating a path replaces the ancestor's rule for it, and every other path keeps inheriting.
/// </summary>
/// <remarks>
/// Not the add/remove shape the path LISTS use. A tolerance is a value, not a membership: "$.total
/// within 0.01" and "$.total within 5" are the same rule with different numbers, and add/remove would
/// make overriding one mean writing it twice.
/// </remarks>
public static class ToleranceResolver
{
    public static IReadOnlyList<ResolvedTolerance> Resolve(IReadOnlyList<ToleranceLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        // Ordered, not a dictionary: the file's order is what a reader sees, and a later level
        // restating a path replaces it WHERE IT STOOD rather than moving it to the end.
        var resolved = new List<ResolvedTolerance>();

        foreach (var layer in layers)
        {
            foreach (var tolerance in layer.Tolerances ?? [])
            {
                var existing = resolved.FindIndex(
                    r => string.Equals(r.Tolerance.Path, tolerance.Path, StringComparison.Ordinal));

                var entry = new ResolvedTolerance(tolerance, layer.Scope, layer.SourceName);
                if (existing >= 0)
                {
                    resolved[existing] = entry;
                }
                else
                {
                    resolved.Add(entry);
                }
            }
        }

        return resolved;
    }
}
