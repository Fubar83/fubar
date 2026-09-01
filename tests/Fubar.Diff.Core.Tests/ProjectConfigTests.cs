using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Settings;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Rules that belong to a repository rather than to a machine.
///
/// The two rules of composition are the whole design, and they are deliberately different from each
/// other: single-value settings are OVERRIDDEN by a later rule, because there is one answer to "how
/// should this be compared"; list settings are ADDED to, because two rules each naming a field to
/// ignore both meant it. Getting that backwards would either make the last rule silently discard the
/// others' ignore lists, or make two rules disagreeing about the mode unresolvable.
/// </summary>
public class ProjectConfigTests
{
    private static ProjectConfig Config(ProjectRule defaults, params ProjectRule[] rules) =>
        new(defaults, rules);

    [Fact]
    public void A_file_matching_nothing_gets_the_defaults()
    {
        var config = Config(
            new ProjectRule { IgnoreWhitespace = true },
            new ProjectRule { Files = "*.json", IgnoreCase = true });

        var resolved = config.For("notes.txt");

        Assert.True(resolved.IgnoreWhitespace);
        Assert.Null(resolved.IgnoreCase);
    }

    [Fact]
    public void A_matching_rule_is_laid_over_the_defaults()
    {
        var config = Config(
            new ProjectRule { IgnoreWhitespace = true },
            new ProjectRule { Files = "*.json", Mode = ComparisonMode.Json });

        var resolved = config.For("/repo/data/config.json");

        Assert.True(resolved.IgnoreWhitespace);
        Assert.Equal(ComparisonMode.Json, resolved.Mode);
    }

    [Fact]
    public void A_later_rule_wins_on_a_single_value_setting()
    {
        var config = Config(
            new ProjectRule(),
            new ProjectRule { Files = "*.js", Mode = ComparisonMode.Auto },
            new ProjectRule { Files = "*.min.js", Mode = ComparisonMode.Text });

        Assert.Equal(ComparisonMode.Text, config.For("bundle.min.js").Mode);
    }

    [Fact]
    public void Every_matching_rule_contributes_to_the_lists()
    {
        // Two rules each naming a field to ignore both meant it. Overriding here would make the
        // narrower rule silently switch off the broader one.
        var config = Config(
            new ProjectRule { IgnoredPaths = ["$.timestamp"] },
            new ProjectRule { Files = "*.json", IgnoredPaths = ["$.requestId"] });

        Assert.Equal(["$.timestamp", "$.requestId"], config.For("a.json").IgnoredPaths);
    }

    [Fact]
    public void Matching_is_by_file_name_rather_than_by_path()
    {
        var config = Config(new ProjectRule(), new ProjectRule { Files = "*.json", IgnoreCase = true });

        Assert.True(config.For("/deep/nested/tree/thing.json").IgnoreCase);
        Assert.Null(config.For("/json/thing.txt").IgnoreCase);
    }

    [Fact]
    public void A_rule_says_nothing_about_what_it_did_not_mention()
    {
        // The reason every setting is nullable. A rule that only sets the mode must not assert
        // defaults for whitespace, case and the rest on the way past.
        var options = new ComparisonOptions { IgnoreWhitespace = true, IgnoreCase = true };

        var applied = new ProjectRule { Mode = ComparisonMode.Text }.ApplyTo(options);

        Assert.Equal(ComparisonMode.Text, applied.Mode);
        Assert.True(applied.IgnoreWhitespace);
        Assert.True(applied.IgnoreCase);
    }

    [Fact]
    public void Applying_adds_to_what_the_session_already_ignores()
    {
        // A path the user chose to ignore for this comparison and a path the repository says is never
        // worth reporting are both true at once.
        var options = new ComparisonOptions
        {
            Json = new Fubar.Diff.Core.Json.JsonComparisonOptions { IgnoredPaths = ["$.sessionOnly"] },
        };

        var applied = new ProjectRule { IgnoredPaths = ["$.fromTheRepo"] }.ApplyTo(options);

        Assert.Equal(["$.sessionOnly", "$.fromTheRepo"], applied.Json.IgnoredPaths);
    }

    [Fact]
    public void Array_keys_from_the_repository_reach_the_comparison()
    {
        var applied = new ProjectRule
        {
            ArrayKeys = new Dictionary<string, string> { ["$.users"] = "id" },
        }.ApplyTo(new ComparisonOptions());

        Assert.Equal("id", applied.Json.ArrayKeyOverrides["$.users"]);
    }

    [Fact]
    public void A_config_that_says_nothing_is_recognisably_empty()
    {
        // What "no file found" resolves to, and what lets a caller skip the whole mechanism.
        Assert.True(ProjectConfig.Empty.IsEmpty);
        Assert.True(ProjectConfig.Empty.For("anything.json").IsEmpty);
    }

    [Fact]
    public void Applying_an_empty_rule_changes_nothing()
    {
        var options = new ComparisonOptions { IgnoreCase = true, Mode = ComparisonMode.Json };

        Assert.Equal(options, new ProjectRule().ApplyTo(options));
    }
}
