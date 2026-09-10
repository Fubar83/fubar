using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Comparison;

/// <summary>Which level of the hierarchy a resolved comparison setting actually came from.</summary>
public enum ComparisonScope
{
    /// <summary>Nothing overrode it anywhere - this is the built-in default.</summary>
    Default,

    /// <summary>Fubar's global user preferences, shared across every workspace.</summary>
    Global,

    /// <summary>A <c>_folder.json</c> between the workspace root and the request.</summary>
    Folder,

    /// <summary>The request's own <c>request.json</c> - or, in the endpoints format, its
    /// <c>endpoint.json</c>. One value for both because it is one level: an endpoint IS the request,
    /// stored under a different name, and splitting the enum would give the same rule two provenances
    /// depending on which format a workspace happens to be in.</summary>
    Request,

    /// <summary>One <c>cases/&lt;name&gt;.json</c> - the innermost containment level.</summary>
    Case,

    /// <summary>
    /// A batch's overlay. NOT a containment level: a batch cuts across the tree, so it is applied
    /// after the chain resolves rather than as a step in it (spec §4.2).
    /// </summary>
    Batch,
}

/// <summary>
/// One setting's effective value plus where it came from, so the UI can say "inherited from Folder:
/// users" next to a control the user has not overridden - the thing that makes a settings hierarchy
/// legible rather than mysterious.
/// </summary>
public readonly record struct Resolved<T>(T Value, ComparisonScope Scope, string SourceName);

/// <summary>
/// One level's contribution, paired with how to describe it. <paramref name="Settings"/> may be null
/// (that level has no comparison section at all), which is treated exactly like a section whose every
/// member is null.
/// </summary>
public sealed record ComparisonSettingsLayer(ComparisonSettings? Settings, ComparisonScope Scope, string SourceName);

/// <summary>
/// One entry of an inherited list, with the level that ADDED it.
///
/// <para>Per entry rather than per list, which is the point of add/remove: a chip in the UI can say
/// "inherited from folder: orders" and its ✕ can know whether removing it means deleting a local
/// addition or writing a removal at this level.</para>
/// </summary>
public readonly record struct ResolvedPath(string Path, ComparisonScope Scope, string SourceName);

/// <summary>Every comparison setting's effective value and origin, after folding the whole chain.</summary>
public sealed record ResolvedComparisonSettings(
    Resolved<bool> IgnoreWhitespace,
    Resolved<bool> IgnoreCase,
    Resolved<bool> NormalizeStructure,
    Resolved<bool> ReportPropertyOrder,
    Resolved<bool> MatchArraysByPosition,
    Resolved<bool> IgnoreNullVsMissing,
    IReadOnlyList<ResolvedPath> IgnoredPaths,
    Resolved<IReadOnlyDictionary<string, string>> ArrayKeyOverrides,
    Resolved<IReadOnlyList<string>> UnorderedArrays,
    Resolved<IReadOnlyList<string>> PositionalArrays)
{
    /// <summary>Just the paths, for the engine, which has no use for where each came from.</summary>
    public IReadOnlyList<string> IgnoredPathValues => [.. IgnoredPaths.Select(p => p.Path)];
}

/// <summary>
/// Folds global → folder(s) → request into one effective set of comparison options.
///
/// Per SETTING, not per level: the closest level that gives a non-null value for that particular
/// setting wins, and every other setting keeps inheriting independently. A request overriding only
/// "ignore whitespace" therefore still picks up a folder's ignore-paths - which is the behaviour that
/// makes the hierarchy worth having, and the reason <see cref="ComparisonSettings"/>' members are all
/// nullable instead of the type being swapped in wholesale.
///
/// Pure and in Core so the precedence rules are testable without a workspace on disk, matching how
/// <c>IInheritanceResolver</c> already handles headers and auth.
/// </summary>
public static class ComparisonSettingsResolver
{
    /// <summary>The built-in values, matching <c>Fubar.Diff.Core.Comparison.ComparisonOptions.Default</c>.</summary>
    private const string DefaultSourceName = "Default";

    /// <summary>
    /// Resolves <paramref name="layers"/>, which must be ordered ROOT-MOST FIRST (global, then each
    /// folder from the workspace root down, then the request) - the same ordering
    /// <c>WorkspaceService.GetInheritanceChainAsync</c> already produces for headers, so that "last one
    /// wins" means "closest to the request wins".
    /// </summary>
    public static ResolvedComparisonSettings Resolve(IReadOnlyList<ComparisonSettingsLayer> layers) => new(
        PickValue(layers, s => s.IgnoreWhitespace, false),
        PickValue(layers, s => s.IgnoreCase, false),
        PickValue(layers, s => s.NormalizeStructure, false),
        PickValue(layers, s => s.ReportPropertyOrder, false),
        PickValue(layers, s => s.MatchArraysByPosition, false),
        PickValue(layers, s => s.IgnoreNullVsMissing, false),
        FoldPaths(layers),
        PickReference<IReadOnlyDictionary<string, string>>(
            layers,
            s => s.ArrayKeyOverrides is { } o ? new Dictionary<string, string>(o) : null,
            new Dictionary<string, string>()),
        PickReference<IReadOnlyList<string>>(layers, s => s.UnorderedArrays is { } u ? [.. u] : null, []),
        PickReference<IReadOnlyList<string>>(layers, s => s.PositionalArrays is { } p ? [.. p] : null, []));

    /// <summary>
    /// Folds an inherited list down the chain: each level's removals, then its additions, keeping the
    /// level that added each surviving entry.
    ///
    /// <para>Unlike every scalar here this is not "closest wins" - a list is built up rather than
    /// chosen, which is the whole difference between add/remove and the replacement this used to do.
    /// A removal that matches nothing is silently fine (see <see cref="InheritedPaths.ApplyTo"/>).</para>
    /// </summary>
    private static IReadOnlyList<ResolvedPath> FoldPaths(IReadOnlyList<ComparisonSettingsLayer> layers)
    {
        var result = new List<ResolvedPath>();

        foreach (var layer in layers)
        {
            if (layer.Settings?.IgnoredPaths is not { } contribution)
            {
                continue;
            }

            foreach (var path in contribution.Remove)
            {
                result.RemoveAll(entry => string.Equals(entry.Path, path, StringComparison.Ordinal));
            }

            foreach (var path in contribution.Add)
            {
                // A level re-adding what it already inherited is not an error and must not duplicate;
                // the deepest level to add it is the one reported, since that is the one a reader would
                // edit to get rid of it.
                result.RemoveAll(entry => string.Equals(entry.Path, path, StringComparison.Ordinal));
                result.Add(new ResolvedPath(path, layer.Scope, layer.SourceName));
            }
        }

        return result;
    }

    private static Resolved<T> PickValue<T>(
        IReadOnlyList<ComparisonSettingsLayer> layers,
        Func<ComparisonSettings, T?> select,
        T fallback)
        where T : struct
    {
        var result = new Resolved<T>(fallback, ComparisonScope.Default, DefaultSourceName);

        foreach (var layer in layers)
        {
            if (layer.Settings is { } settings && select(settings) is { } value)
            {
                result = new Resolved<T>(value, layer.Scope, layer.SourceName);
            }
        }

        return result;
    }

    private static Resolved<T> PickReference<T>(
        IReadOnlyList<ComparisonSettingsLayer> layers,
        Func<ComparisonSettings, T?> select,
        T fallback)
        where T : class
    {
        var result = new Resolved<T>(fallback, ComparisonScope.Default, DefaultSourceName);

        foreach (var layer in layers)
        {
            if (layer.Settings is { } settings && select(settings) is { } value)
            {
                result = new Resolved<T>(value, layer.Scope, layer.SourceName);
            }
        }

        return result;
    }
}
