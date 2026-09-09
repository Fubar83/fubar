using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Infrastructure;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The one place a response is judged. Its counting rules were wrong twice before they lived here -
/// hunks where semantic changes were meant, and ignored changes counted as differences - each time
/// producing a row that disagreed with the pane beside it.
/// </summary>
public class ResponseComparerTests
{
    private static IResponseComparer Comparer()
    {
        var services = new ServiceCollection();
        services.AddFubarDiffTextAndJson();
        services.AddSingleton<JsonSemanticPass>();
        services.AddSingleton<IFileComparisonService, FileComparisonService>();
        services.AddSingleton<IResponseComparer, DiffResponseComparer>();
        return services.BuildServiceProvider().GetRequiredService<IResponseComparer>();
    }

    private static ResolvedComparisonSettings Settings(params string[] ignored) =>
        ComparisonSettingsResolver.Resolve([
            new ComparisonSettingsLayer(
                new ComparisonSettings { IgnoredPaths = InheritedPaths.FromAdded(ignored) },
                ComparisonScope.Request,
                "Request"),
        ]);

    [Fact]
    public async Task Identical_responses_are_the_same()
    {
        var outcome = await Comparer().CompareAsync("""{"a":1}""", """{"a":1}""", Settings(), TestContext.Current.CancellationToken);

        Assert.True(outcome.Same);
        Assert.Equal(0, outcome.DifferenceCount);
    }

    /// <summary>JSON objects are unordered, so a reordering is not a difference by default - the
    /// property that makes comparing two services' answers useful at all.</summary>
    [Fact]
    public async Task Key_order_alone_is_not_a_difference()
    {
        var outcome = await Comparer().CompareAsync(
            """{"a":1,"b":2}""", """{"b":2,"a":1}""", Settings(), TestContext.Current.CancellationToken);

        Assert.True(outcome.Same);
    }

    [Fact]
    public async Task A_changed_value_is_one_difference_and_names_its_path()
    {
        var outcome = await Comparer().CompareAsync(
            """{"a":1,"b":2}""", """{"a":1,"b":3}""", Settings(), TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.DifferenceCount);
        Assert.True(outcome.IsSemantic);

        var difference = Assert.Single(outcome.Differences);
        Assert.Equal("$.b", difference.Path);
        Assert.Equal(ResponseDifferenceKind.Changed, difference.Kind);
    }

    /// <summary>
    /// The bug this type exists to make impossible. The engine MARKS an ignored change rather than
    /// dropping it, so a count over every change reports exactly the differences the rules were written
    /// to remove.
    /// </summary>
    [Fact]
    public async Task An_ignored_difference_does_not_count()
    {
        var left = """{"generatedAt":"2026-01-01","total":10}""";
        var right = """{"generatedAt":"2026-06-30","total":10}""";

        Assert.Equal(1, (await Comparer().CompareAsync(left, right, Settings(), TestContext.Current.CancellationToken)).DifferenceCount);
        Assert.True((await Comparer().CompareAsync(left, right, Settings("$.generatedAt"), TestContext.Current.CancellationToken)).Same);
    }

    [Fact]
    public async Task An_added_and_a_removed_property_are_reported_as_such()
    {
        var outcome = await Comparer().CompareAsync(
            """{"a":1,"gone":2}""", """{"a":1,"fresh":3}""", Settings(), TestContext.Current.CancellationToken);

        Assert.Contains(outcome.Differences, d => d.Kind == ResponseDifferenceKind.Added && d.Path == "$.fresh");
        Assert.Contains(outcome.Differences, d => d.Kind == ResponseDifferenceKind.Removed && d.Path == "$.gone");
    }

    /// <summary>Not everything a response returns is JSON. Text falls back to hunks, and says so rather
    /// than inventing paths it cannot know.</summary>
    [Fact]
    public async Task Text_that_is_not_json_is_compared_as_text()
    {
        var outcome = await Comparer().CompareAsync("hello\nworld", "hello\nthere", Settings(), TestContext.Current.CancellationToken);

        Assert.False(outcome.IsSemantic);
        Assert.Equal(1, outcome.DifferenceCount);
        Assert.Empty(outcome.Differences);
    }
}
