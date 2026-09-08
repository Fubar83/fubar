using Fubar.Studio.Core.Comparison;

namespace Fubar.Studio.Core.Snapshots;

/// <summary>One level's snapshot policy, and how to describe where it came from.</summary>
public sealed record SnapshotPolicyLayer(SnapshotPolicy? Policy, ComparisonScope Scope, string SourceName);

/// <summary>
/// Folds global → folder(s) → request into the rules that apply when a snapshot is recorded.
///
/// <para>Add/remove down the chain, the same shape ignore rules use (<see cref="InheritedPaths"/>) and
/// for the same reason: a request that needs one extra redaction must not have to restate its folder's,
/// and must not silently stop inheriting them when it does.</para>
///
/// <para>Headers are the exception - a plain list, closest level wins. There is no "add one header to
/// what the folder keeps" case worth the shape, and a level that names headers is stating the whole
/// set it wants recorded.</para>
/// </summary>
public static class SnapshotPolicyResolver
{
    public static ResolvedSnapshotPolicy Resolve(IReadOnlyList<SnapshotPolicyLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var redact = new List<SnapshotRule>();
        var normalize = new List<SnapshotRule>();
        IReadOnlyList<string> headers = [];

        foreach (var layer in layers)
        {
            if (layer.Policy is not { } policy)
            {
                continue;
            }

            Apply(redact, policy.Redact);
            Apply(normalize, policy.Normalize);

            if (policy.Headers is { } named)
            {
                headers = [.. named];
            }
        }

        return new ResolvedSnapshotPolicy(redact, normalize, headers);
    }

    private static void Apply(List<SnapshotRule> rules, InheritedRules? contribution)
    {
        if (contribution is null)
        {
            return;
        }

        foreach (var path in contribution.Remove)
        {
            rules.RemoveAll(r => string.Equals(r.Path, path, StringComparison.Ordinal));
        }

        foreach (var rule in contribution.Add)
        {
            // Re-stating a path replaces it: a level saying "$..token becomes <gone>" over an
            // ancestor's "<redacted>" means the deeper one, not both.
            rules.RemoveAll(r => string.Equals(r.Path, rule.Path, StringComparison.Ordinal));
            rules.Add(rule);
        }
    }
}
