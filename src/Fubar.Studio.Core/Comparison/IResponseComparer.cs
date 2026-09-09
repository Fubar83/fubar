namespace Fubar.Studio.Core.Comparison;

/// <summary>How one difference changed the response.</summary>
public enum ResponseDifferenceKind
{
    Added,
    Removed,
    Changed,
}

/// <summary>One difference, in terms Core can state without knowing what compared them.</summary>
/// <param name="Path">JSON path when the comparison was semantic; a line reference otherwise.</param>
public sealed record ResponseDifference(
    string Path,
    string? Left,
    string? Right,
    ResponseDifferenceKind Kind);

/// <summary>
/// What comparing two responses concluded.
/// </summary>
/// <param name="DifferenceCount">
/// Differences that COUNT - the ignored ones are already excluded. The engine marks an ignored
/// difference rather than dropping it, and a count that included them would report exactly the
/// differences the rules were written to remove.
/// </param>
public sealed record ComparisonOutcome(
    int DifferenceCount,
    bool IsSemantic,
    IReadOnlyList<ResponseDifference> Differences)
{
    public static readonly ComparisonOutcome Identical = new(0, false, []);

    public bool Same => DifferenceCount == 0;
}

/// <summary>
/// Compares two responses under a resolved set of rules.
///
/// <para>The seam that lets a run JUDGE a response without knowing what does the comparing. Every
/// oracle - a snapshot, another environment, a previous run - differs only in where the other side
/// comes from, so they all end here; a new oracle must never bring a second comparison
/// implementation with it (docs/spec-endpoints.md §5).</para>
///
/// <para>Deliberately free of diff types in its signature. <c>Fubar.Studio.Core</c> depends on nothing
/// but the BCL, and the engine lives behind an adapter, so the collection runner and the CLI can
/// compare without either of them referencing it.</para>
/// </summary>
public interface IResponseComparer
{
    Task<ComparisonOutcome> CompareAsync(
        string left,
        string right,
        ResolvedComparisonSettings settings,
        CancellationToken cancellationToken = default);
}
