using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Studio.Core.Comparison;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// <see cref="IResponseComparer"/> over the diff engine.
///
/// <para>The single place a response is judged. Everything that used to count differences for itself -
/// the environment-comparison window did, with its own idea of what counted - goes through this, so a
/// verdict in the list, a verdict in a CLI report and a verdict in the pane cannot disagree.</para>
/// </summary>
/// <remarks>
/// <para><b>Why this lives in the UI project rather than Infrastructure</b>, where the spec first put
/// it. It needs <see cref="ComparisonSettingsMapper"/>, which the request editor's pane also needs to
/// render with - and a view model may not reference Infrastructure (CLAUDE.md). Moving the mapper
/// would either duplicate it, and let the pane and the verdict drift apart on the very settings they
/// are meant to share, or breach that rule.</para>
///
/// <para>Nothing is lost by it: <c>Fubar.Studio.UI</c> is the executable, so <c>--run</c> reaches this
/// through the same composition root a window does. The seam that mattered is the PORT, which lives in
/// Core - the runner and every oracle depend on that and stay free of the engine.</para>
/// </remarks>
public sealed class DiffResponseComparer : IResponseComparer
{
    private readonly IFileComparisonService _comparison;

    public DiffResponseComparer(IFileComparisonService comparison)
    {
        _comparison = comparison;
    }

    public async Task<ComparisonOutcome> CompareAsync(
        string left,
        string right,
        ResolvedComparisonSettings settings,
        CancellationToken cancellationToken = default)
    {
        // Identical text is identical data, so nothing is learned by comparing it - and a response of
        // any size is cheap to rule out this way.
        if (string.Equals(left, right, System.StringComparison.Ordinal))
        {
            return ComparisonOutcome.Identical;
        }

        var comparison = await _comparison
            .CompareTextAsync(left, right, ComparisonSettingsMapper.ToOptions(settings), "left", "right", cancellationToken)
            .ConfigureAwait(false);

        if (!comparison.IsSemantic)
        {
            // Text mode: a hunk is the coarsest honest unit. There are no paths to name, so the
            // differences list stays empty rather than inventing line numbers as paths.
            return new ComparisonOutcome(comparison.Result.Hunks.Count, false, []);
        }

        // Ignored changes are MARKED, not dropped, so counting them all would report the differences
        // the rules exist to remove.
        var counted = comparison.SemanticChanges.Where(c => !c.IsIgnored).ToList();

        return new ComparisonOutcome(counted.Count, true, [.. counted.Select(Describe)]);
    }

    private static ResponseDifference Describe(JsonChange change) => new(
        change.Path.ToString(),
        change.Left?.ToString(),
        change.Right?.ToString(),
        change.Kind switch
        {
            ChangeKind.Inserted => ResponseDifferenceKind.Added,
            ChangeKind.Deleted => ResponseDifferenceKind.Removed,
            _ => ResponseDifferenceKind.Changed,
        });
}
