using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Infrastructure;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// What the diff window WRITES when a rule is added or removed - which is the half that was wrong.
///
/// <para>The resolver's behaviour is covered in <c>ComparisonSettingsResolverTests</c>. This is about
/// the view model: it used to save the RESOLVED list as a request-level override, so adding one rule
/// to a request that inherited three copied all four onto the request and quietly ended inheritance
/// for that setting. Nothing on screen said so, and the folder's rules stopped reaching it.</para>
/// </summary>
public class IgnoreRuleInheritanceTests
{
    private static DiffSettingsContext ContextInheritingFromFolder(params string[] folderPaths) =>
        new(
            [new ComparisonSettingsLayer(
                new ComparisonSettings { IgnoredPaths = InheritedPaths.FromAdded(folderPaths) },
                ComparisonScope.Folder,
                "Folder: orders")],
            RequestOverrides: null,
            FolderName: "orders",
            SaveAsync: (_, _) => Task.CompletedTask);

    /// <summary>The real engine, wired as Composition does: these tests are about what the view model
    /// WRITES, and a stubbed comparison would let a wrong resolve pass unnoticed.</summary>
    private static IFileComparisonService Comparison()
    {
        var services = new ServiceCollection();
        services.AddFubarDiffTextAndJson();
        services.AddSingleton<JsonSemanticPass>();
        services.AddSingleton<IFileComparisonService, FileComparisonService>();
        return services.BuildServiceProvider().GetRequiredService<IFileComparisonService>();
    }

    private static async Task<DiffPreviewViewModel> OpenAsync(DiffSettingsContext context)
    {
        var vm = new DiffPreviewViewModel(Comparison());
        await vm.LoadAsync("{}", "{}", "left", "right", "t", context);
        return vm;
    }

    [Fact]
    public async Task An_inherited_rule_shows_where_it_came_from()
    {
        var vm = await OpenAsync(ContextInheritingFromFolder("$.traceId"));

        var chip = Assert.Single(vm.IgnoredPaths);
        Assert.Equal("$.traceId", chip.Path);
        Assert.True(chip.IsInherited);
        Assert.Equal("from Folder: orders", chip.Source);
    }

    /// <summary>The defect this step exists to fix: adding one rule must write ONE rule.</summary>
    [Fact]
    public async Task Adding_a_rule_does_not_copy_the_inherited_ones_onto_the_request()
    {
        var vm = await OpenAsync(ContextInheritingFromFolder("$.traceId", "$..timestamp"));

        await vm.IgnorePathCommand.ExecuteAsync("$.meta.requestId");

        // Both are in force...
        Assert.Equal(
            ["$.traceId", "$..timestamp", "$.meta.requestId"],
            vm.IgnoredPaths.Select(p => p.Path));

        // ...but only the new one was written here, so the folder's rules keep arriving.
        Assert.Equal(["$.meta.requestId"], vm.PendingOverrides.IgnoredPaths!.Add);
        Assert.Empty(vm.PendingOverrides.IgnoredPaths!.Remove);
    }

    [Fact]
    public async Task Removing_an_inherited_rule_writes_a_removal_rather_than_editing_the_folder()
    {
        var vm = await OpenAsync(ContextInheritingFromFolder("$.traceId", "$..timestamp"));

        await vm.RemoveIgnoreCommand.ExecuteAsync("$.traceId");

        Assert.Equal(["$..timestamp"], vm.IgnoredPaths.Select(p => p.Path));
        Assert.Equal(["$.traceId"], vm.PendingOverrides.IgnoredPaths!.Remove);
        Assert.Empty(vm.PendingOverrides.IgnoredPaths!.Add);
    }

    /// <summary>A rule added here and then removed leaves nothing behind - not a removal of something
    /// that was never inherited, which would sit in the file forever saying nothing.</summary>
    [Fact]
    public async Task Removing_a_rule_added_here_just_drops_it()
    {
        var vm = await OpenAsync(ContextInheritingFromFolder("$.traceId"));

        await vm.IgnorePathCommand.ExecuteAsync("$.meta.requestId");
        await vm.RemoveIgnoreCommand.ExecuteAsync("$.meta.requestId");

        Assert.Equal(["$.traceId"], vm.IgnoredPaths.Select(p => p.Path));
        Assert.Empty(vm.PendingOverrides.IgnoredPaths!.Add);
        Assert.Empty(vm.PendingOverrides.IgnoredPaths!.Remove);
    }

    [Fact]
    public async Task A_rule_added_here_is_not_marked_inherited()
    {
        var vm = await OpenAsync(ContextInheritingFromFolder());

        await vm.IgnorePathCommand.ExecuteAsync("$.meta.requestId");

        var chip = Assert.Single(vm.IgnoredPaths);
        Assert.False(chip.IsInherited);
        Assert.Equal("set here", chip.Source);
    }
}
