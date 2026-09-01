using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Infrastructure.Settings;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// Finding <c>.fubardiff.json</c> on disk.
///
/// Two behaviours carry the weight. It walks UP from the file being compared, which is what makes it
/// work in a monorepo and what every tool keeping rules beside code does. And a broken config is
/// reported and then ignored - refusing to compare two files because a rules file has a trailing
/// comma would be the wrong trade every time, but so would leaving the user wondering why their rules
/// stopped working.
/// </summary>
public class ProjectConfigStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fubar-config-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public void No_config_anywhere_is_simply_no_rules()
    {
        var file = Write("src/a.json", "{}");

        var config = new FileSystemProjectConfigStore().Find(file, out var problem);

        Assert.True(config.IsEmpty);
        Assert.Null(problem);
    }

    [Fact]
    public void A_config_beside_the_file_is_found()
    {
        Write(".fubardiff.json", """{ "ignoreWhitespace": true }""");
        var file = Write("a.json", "{}");

        Assert.True(new FileSystemProjectConfigStore().Find(file, out _).For(file).IgnoreWhitespace);
    }

    [Fact]
    public void A_config_at_the_root_governs_a_file_deep_in_the_tree()
    {
        // The monorepo case, and the reason this walks up at all.
        Write(".fubardiff.json", """{ "ignoreCase": true }""");
        var file = Write("src/deep/nested/a.json", "{}");

        Assert.True(new FileSystemProjectConfigStore().Find(file, out _).For(file).IgnoreCase);
    }

    [Fact]
    public void The_nearest_config_wins_outright()
    {
        // Not merged with the ones above it: "the file you are looking at is the file that applies" is
        // the simpler promise, and the one every editor's config makes.
        Write(".fubardiff.json", """{ "ignoreCase": true }""");
        Write("src/.fubardiff.json", """{ "ignoreWhitespace": true }""");
        var file = Write("src/a.json", "{}");

        var rule = new FileSystemProjectConfigStore().Find(file, out _).For(file);

        Assert.True(rule.IgnoreWhitespace);
        Assert.Null(rule.IgnoreCase);
    }

    [Fact]
    public void Rules_read_their_patterns_paths_and_keys()
    {
        Write(".fubardiff.json", """
            {
              "rules": [
                {
                  "files": "*.json",
                  "mode": "json",
                  "ignoredPaths": ["$.requestId", "$.timestamp"],
                  "arrayKeys": { "$.users": "id" }
                }
              ]
            }
            """);

        var file = Write("snapshot.json", "{}");
        var rule = new FileSystemProjectConfigStore().Find(file, out _).For(file);

        Assert.Equal(ComparisonMode.Json, rule.Mode);
        Assert.Equal(["$.requestId", "$.timestamp"], rule.IgnoredPaths);
        Assert.Equal("id", rule.ArrayKeys["$.users"]);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        // It is a config file people edit by hand, and JSON's strictness about both is a papercut
        // every other tool in this space has decided to absorb.
        Write(".fubardiff.json", """
            {
              // our snapshots carry a request id that changes every run
              "ignoredPaths": ["$.requestId"],
            }
            """);

        var file = Write("a.json", "{}");

        Assert.Equal(["$.requestId"], new FileSystemProjectConfigStore().Find(file, out _).For(file).IgnoredPaths);
    }

    [Fact]
    public void A_broken_config_is_reported_and_then_ignored()
    {
        Write(".fubardiff.json", """{ "rules": [ """);
        var file = Write("a.json", "{}");

        var config = new FileSystemProjectConfigStore().Find(file, out var problem);

        Assert.True(config.IsEmpty);
        Assert.NotNull(problem);
        Assert.Contains(".fubardiff.json", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rule_with_no_files_pattern_is_dropped_rather_than_applied_to_everything()
    {
        // Which is what a typo in "files" would otherwise do - quietly, to every comparison.
        Write(".fubardiff.json", """{ "rules": [ { "fies": "*.json", "ignoreCase": true } ] }""");
        var file = Write("a.json", "{}");

        Assert.Null(new FileSystemProjectConfigStore().Find(file, out _).For(file).IgnoreCase);
    }

    [Fact]
    public void An_unknown_mode_loses_that_line_rather_than_the_file()
    {
        // A config written for a later version naming a format this build does not have.
        Write(".fubardiff.json", """{ "mode": "toml", "ignoreCase": true }""");
        var file = Write("a.json", "{}");

        var rule = new FileSystemProjectConfigStore().Find(file, out _).For(file);

        Assert.Null(rule.Mode);
        Assert.True(rule.IgnoreCase);
    }

    [Fact]
    public void A_path_that_does_not_exist_is_answered_rather_than_thrown_at()
    {
        var config = new FileSystemProjectConfigStore().Find(
            Path.Combine(_root, "nowhere", "missing.json"),
            out var problem);

        Assert.True(config.IsEmpty);
        Assert.Null(problem);
    }

    [Fact]
    public void No_path_at_all_is_no_rules()
    {
        Assert.True(new FileSystemProjectConfigStore().Find(null, out _).IsEmpty);
        Assert.True(new FileSystemProjectConfigStore().Find("   ", out _).IsEmpty);
    }
}
