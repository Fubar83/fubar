using System.Text.Json;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Snapshots;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Tests.Json;

/// <summary>
/// What a workspace document may NOT contain.
///
/// <para>These files are committed and read in diffs, and <c>System.Text.Json</c> serialises every
/// public getter - so a convenience property added for the code's own use lands in everybody's
/// repository. It had already happened: saving an endpoint with a snapshot policy wrote
/// <c>"isEmpty": false</c> twice, and a request carrying a tolerance would have written the
/// <c>kind</c> the code derives from the rule rather than the rule.</para>
///
/// <para>Round-tripping is not enough to catch this - the extra members deserialise back to nothing,
/// so every existing test still passed. The file's TEXT is what is wrong.</para>
/// </summary>
public class ComputedPropertiesTests
{
    private static string Write<T>(T value) => JsonSerializer.Serialize(value, FubarJson.Options);

    [Fact]
    public void A_snapshot_policy_writes_only_its_rules()
    {
        var json = Write(new RequestModel
        {
            Name = "Get order",
            Snapshot = new SnapshotPolicy
            {
                Redact = new InheritedRules { Add = [new SnapshotRule("$..token", "<redacted>")] },
            },
        });

        Assert.DoesNotContain("isEmpty", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$..token", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Tolerance.Kind</c> is DERIVED - it reports whether the rule states exactly one allowance -
    /// so writing it would put the answer in the file beside the question, and a hand-edited file
    /// would carry a stale one.
    /// </summary>
    [Fact]
    public void A_tolerance_writes_only_what_it_allows()
    {
        // The tolerance alone: a RequestModel has a legitimate "kind" of its own, which would make
        // this assertion pass or fail for the wrong reason.
        var json = Write(new Tolerance { Path = "$.total", Numeric = 0.01 });

        Assert.DoesNotContain("\"kind\"", json, StringComparison.Ordinal);
        Assert.Contains("\"numeric\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Comparison_settings_write_only_what_this_level_overrides()
    {
        var json = Write(new ComparisonSettings
        {
            IgnoreCase = true,
            IgnoredPaths = new InheritedPaths { Add = ["$.meta.requestId"] },
        });

        Assert.DoesNotContain("isEmpty", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_batch_overlay_writes_only_its_rules()
    {
        var json = Write(new Batch
        {
            Name = "smoke",
            Overlay = new BatchOverlay { Tolerances = [new Tolerance { Path = "$.total", Numeric = 1 }] },
        });

        Assert.DoesNotContain("isEmpty", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_environments_transport_writes_only_its_settings()
    {
        var json = Write(new WorkspaceEnvironment
        {
            Name = "Staging",
            Transport = new TransportSettings { ProxyUrl = "http://localhost:8888" },
        });

        Assert.DoesNotContain("isEmpty", json, StringComparison.OrdinalIgnoreCase);
    }
}
