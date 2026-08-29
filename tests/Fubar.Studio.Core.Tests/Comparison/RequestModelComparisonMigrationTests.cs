using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Comparison;

/// <summary>
/// Back-compat for request.json files written before comparison settings became a hierarchy, when the
/// only option was a flat <see cref="RequestModel.ResponseDiffIgnorePaths"/> list.
/// </summary>
public class RequestModelComparisonMigrationTests
{
    [Fact]
    public void A_request_with_neither_shape_has_no_comparison_settings()
    {
        Assert.Null(new RequestModel { Name = "r" }.EffectiveComparison);
    }

    [Fact]
    public void A_legacy_ignore_list_reads_as_comparison_settings()
    {
        var request = new RequestModel { Name = "r", ResponseDiffIgnorePaths = ["$.meta.requestId"] };

        Assert.Equal(["$.meta.requestId"], request.EffectiveComparison!.IgnoredPaths);
    }

    [Fact]
    public void The_new_section_wins_when_both_are_present()
    {
        // Only reachable from a hand-edited file, but it must not silently merge the two.
        var request = new RequestModel
        {
            Name = "r",
            ResponseDiffIgnorePaths = ["$.legacy"],
            Comparison = new ComparisonSettings { IgnoredPaths = ["$.current"] },
        };

        Assert.Equal(["$.current"], request.EffectiveComparison!.IgnoredPaths);
    }

    [Fact]
    public void Migrating_moves_the_legacy_list_into_the_new_section_and_clears_it()
    {
        var request = new RequestModel { Name = "r", ResponseDiffIgnorePaths = ["$.a", "$.b"] };

        request.MigrateLegacyIgnorePaths();

        Assert.Equal(["$.a", "$.b"], request.Comparison!.IgnoredPaths);
        Assert.Empty(request.ResponseDiffIgnorePaths);
    }

    [Fact]
    public void Migrating_never_overwrites_settings_that_are_already_in_the_new_shape()
    {
        var request = new RequestModel
        {
            Name = "r",
            ResponseDiffIgnorePaths = ["$.legacy"],
            Comparison = new ComparisonSettings { IgnoreCase = true },
        };

        request.MigrateLegacyIgnorePaths();

        Assert.True(request.Comparison!.IgnoreCase);
        Assert.Null(request.Comparison.IgnoredPaths);
        Assert.Empty(request.ResponseDiffIgnorePaths);
    }

    [Fact]
    public void Migrating_an_already_migrated_request_is_a_no_op()
    {
        var request = new RequestModel { Name = "r", Comparison = new ComparisonSettings { IgnoredPaths = ["$.a"] } };

        request.MigrateLegacyIgnorePaths();

        Assert.Equal(["$.a"], request.Comparison!.IgnoredPaths);
    }
}
