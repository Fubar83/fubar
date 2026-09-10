using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Choosing how an array is matched, from API Studio's own comparison windows.
///
/// <para>The tree's "Compare this list" menu is part of the shared widget and has always RENDERED
/// here; the widget only raises an event, because the host owns the options. Nothing in API Studio
/// listened, so the menu recorded a choice nobody applied - the check mark never moved and the
/// comparison never changed. Fubar Diff had the other half all along.</para>
/// </summary>
public class ArrayMatchingTests
{
    /// <summary>The real engine, wired as Composition does: these tests are about what reaches the
    /// differ, and a stub would let a wrong mapping pass unnoticed.</summary>
    private static IFileComparisonService Comparison()
    {
        var services = new ServiceCollection();
        services.AddFubarDiffTextAndJson();
        services.AddSingleton<JsonSemanticPass>();
        services.AddSingleton<IFileComparisonService, FileComparisonService>();
        return services.BuildServiceProvider().GetRequiredService<IFileComparisonService>();
    }

    /// <summary>The two answers from the report: one element prepended to a list of strings.</summary>
    private const string Source = """{"name":"Henrik","somearray":["one","two"]}""";

    private const string Target = """{"name":"Henrik","somearray":["three","one","two"],"added":true}""";

    private static DiffSettingsContext Context(ComparisonSettingsLayer[] inherited) =>
        new(inherited, RequestOverrides: null, FolderName: "orders", SaveAsync: (_, _) => Task.CompletedTask);

    private static async Task<DiffPreviewViewModel> OpenAsync(params ComparisonSettingsLayer[] inherited)
    {
        var vm = new DiffPreviewViewModel(Comparison());
        await vm.LoadAsync(Source, Target, "Staging", "Production", "somearray", Context(inherited));
        return vm;
    }

    private static int ReportedInTheArray(DiffPreviewViewModel vm) =>
        vm.Pane.SemanticChanges.Count(
            c => !c.IsIgnored && c.Path.ToString().StartsWith("$.somearray", StringComparison.Ordinal));

    // ---- The case in the report -----------------------------------------------------------------

    /// <summary>
    /// The whole point: with order ignored, the element that was added is the ONLY thing reported for
    /// that list. "one" and "two" have new indices and have not changed.
    /// </summary>
    [Fact]
    public async Task Ignoring_order_reports_only_the_added_element()
    {
        var vm = await OpenAsync();

        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Unordered);

        var change = Assert.Single(
            vm.Pane.SemanticChanges,
            c => c.Path.ToString().StartsWith("$.somearray", StringComparison.Ordinal));

        Assert.Equal(ChangeKind.Inserted, change.Kind);
        Assert.False(change.IsIgnored);
    }

    /// <summary>Before the choice the array is positional, and every element after the insertion reads
    /// as changed - which is what makes wiring the menu up worth doing.</summary>
    [Fact]
    public async Task Without_the_choice_the_array_is_compared_by_position()
    {
        var vm = await OpenAsync();

        Assert.True(ReportedInTheArray(vm) > 1);
    }

    // ---- What it writes -------------------------------------------------------------------------

    [Fact]
    public async Task The_choice_becomes_a_request_level_override()
    {
        var vm = await OpenAsync();

        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Unordered);

        Assert.Equal(["$.somearray"], vm.PendingOverrides.UnorderedArrays);
        Assert.True(vm.SettingsDirty);
        Assert.True(vm.HasOverrides);
    }

    /// <summary>An array can only be matched one way. A stale entry in another list would make the
    /// menu's check mark lie, and hand the differ a contradiction nobody wrote.</summary>
    [Fact]
    public async Task Choosing_a_second_way_replaces_the_first()
    {
        var vm = await OpenAsync();

        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Unordered);
        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Position);

        Assert.Empty(vm.PendingOverrides.UnorderedArrays!);
        Assert.Equal(["$.somearray"], vm.PendingOverrides.PositionalArrays);
    }

    [Fact]
    public async Task Matching_by_a_key_replaces_an_order_rule_too()
    {
        var vm = await OpenAsync();

        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Unordered);
        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Key, "id");

        Assert.Empty(vm.PendingOverrides.UnorderedArrays!);
        Assert.Equal("id", vm.PendingOverrides.ArrayKeyOverrides!["$.somearray"]);
    }

    /// <summary>
    /// These lists REPLACE what they inherit rather than adding to it, so writing only the array just
    /// chosen would silently drop every rule the folder above states about the others.
    /// </summary>
    [Fact]
    public async Task A_folders_rule_about_another_array_survives()
    {
        var vm = await OpenAsync(new ComparisonSettingsLayer(
            new ComparisonSettings { UnorderedArrays = ["$.tags"] },
            ComparisonScope.Folder,
            "Folder: orders"));

        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Unordered);

        Assert.Equal(["$.tags", "$.somearray"], vm.PendingOverrides.UnorderedArrays);
    }

    // ---- The chips ------------------------------------------------------------------------------

    /// <summary>
    /// Not decoration. Ignoring an array's order usually removes every row that array had from the
    /// tree - that is the point - and the menu that set the rule lives on those rows, so without a
    /// chip the instruction becomes unreachable the moment it works.
    /// </summary>
    [Fact]
    public async Task A_rule_shows_as_a_chip_saying_what_it_does()
    {
        var vm = await OpenAsync();

        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Unordered);

        var chip = Assert.Single(vm.ArrayRules);
        Assert.Equal("$.somearray", chip.Path);
        Assert.Equal("order ignored", chip.How);
        Assert.False(chip.IsInherited);
    }

    [Fact]
    public async Task An_inherited_rule_says_where_it_came_from()
    {
        var vm = await OpenAsync(new ComparisonSettingsLayer(
            new ComparisonSettings { UnorderedArrays = ["$.somearray"] },
            ComparisonScope.Folder,
            "Folder: orders"));

        var chip = Assert.Single(vm.ArrayRules);
        Assert.True(chip.IsInherited);
        Assert.Equal("from Folder: orders", chip.Source);
    }

    [Fact]
    public async Task The_chips_cross_puts_the_array_back()
    {
        var vm = await OpenAsync();

        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Unordered);
        await vm.ResetArrayMatchCommand.ExecuteAsync("$.somearray");

        Assert.Empty(vm.ArrayRules);
        Assert.True(ReportedInTheArray(vm) > 1);
    }

    // ---- Reading, as opposed to judging ----------------------------------------------------------

    /// <summary>
    /// The View menu's "Compare as". Text mode is how you look at the bytes when you do not trust the
    /// parse, and it must not become a saved property of the request.
    /// </summary>
    [Fact]
    public async Task Comparing_as_text_changes_the_view_without_overriding_anything()
    {
        var vm = await OpenAsync();

        vm.SetCompareModeCommand.Execute(ComparisonMode.Text);

        Assert.True(vm.IsModeText);
        Assert.False(vm.IsModeAuto);
        Assert.False(vm.HasOverrides);
    }
}
