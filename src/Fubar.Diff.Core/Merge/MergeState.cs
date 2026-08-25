using System.Collections.Generic;
using System.Linq;

namespace Fubar.Diff.Core.Merge;

/// <summary>
/// The user's decisions about each hunk, keyed by hunk index. Immutable: every resolution produces a
/// new state, so the view model can hold one field and re-render from it without worrying about who
/// else has a reference.
///
/// Deliberately keyed by index rather than storing a resolution ON the hunk: hunks are derived from
/// the diff and are rebuilt whenever comparison options change, whereas decisions should survive that
/// (see <see cref="RemapTo"/>).
/// </summary>
public sealed class MergeState
{
    private readonly IReadOnlyDictionary<int, HunkResolution> _resolutions;

    private MergeState(IReadOnlyDictionary<int, HunkResolution> resolutions) =>
        _resolutions = resolutions;

    /// <summary>Nothing decided yet.</summary>
    public static MergeState Empty { get; } = new(new Dictionary<int, HunkResolution>());

    /// <summary>True when the user has resolved at least one hunk, i.e. there is something to save.</summary>
    public bool HasResolutions => _resolutions.Count > 0;

    /// <summary>How many hunks have been resolved.</summary>
    public int ResolvedCount => _resolutions.Count;

    /// <summary>The decision for a hunk, or <see cref="HunkResolution.Unresolved"/>.</summary>
    public HunkResolution For(int hunkIndex) =>
        _resolutions.TryGetValue(hunkIndex, out var resolution) ? resolution : HunkResolution.Unresolved;

    /// <summary>
    /// Returns a state with <paramref name="hunkIndex"/> resolved. Setting
    /// <see cref="HunkResolution.Unresolved"/> clears the decision rather than storing it, so
    /// <see cref="HasResolutions"/> stays honest about whether anything is pending.
    /// </summary>
    public MergeState With(int hunkIndex, HunkResolution resolution)
    {
        var next = new Dictionary<int, HunkResolution>(_resolutions);

        if (resolution == HunkResolution.Unresolved)
        {
            next.Remove(hunkIndex);
        }
        else
        {
            next[hunkIndex] = resolution;
        }

        return new MergeState(next);
    }

    /// <summary>Drops every decision.</summary>
    public MergeState Clear() => Empty;

    /// <summary>
    /// Drops decisions for hunks that no longer exist. Toggling a comparison option re-runs the diff
    /// and can produce fewer hunks; without this, a stale index would silently resolve the wrong hunk
    /// - or throw when <c>MergedDocument</c> looked it up.
    /// </summary>
    public MergeState RemapTo(int hunkCount) =>
        _resolutions.Keys.All(i => i < hunkCount)
            ? this
            : new MergeState(_resolutions
                .Where(pair => pair.Key < hunkCount)
                .ToDictionary(pair => pair.Key, pair => pair.Value));
}
