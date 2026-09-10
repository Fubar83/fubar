using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Infrastructure;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The chrome around API Studio's diff view - the toolbar, the View menu and the rule chips.
///
/// <para>These are rendered, not just constructed. Compiled bindings are NOT on in this project, so a
/// mistyped binding path is silent at runtime: the control appears, does nothing, and no test that
/// only touches the view model would notice. Every assertion here is about a control the XAML claims
/// to have wired up.</para>
/// </summary>
public class ComparisonWindowChromeTests
{
    private static IFileComparisonService Comparison()
    {
        var services = new ServiceCollection();
        services.AddFubarDiffTextAndJson();
        services.AddSingleton<JsonSemanticPass>();
        services.AddSingleton<IFileComparisonService, FileComparisonService>();
        return services.BuildServiceProvider().GetRequiredService<IFileComparisonService>();
    }

    private static DiffSettingsContext Context() =>
        new([], RequestOverrides: null, FolderName: "orders", SaveAsync: (_, _) => Task.CompletedTask);

    /// <summary>Opens the dialog on a comparison that already carries both kinds of rule, so the chip
    /// templates actually run.</summary>
    private static async Task<DiffPreviewDialog> OpenAsync()
    {
        var vm = new DiffPreviewViewModel(Comparison());
        await vm.LoadAsync(
            """{"somearray":["one","two"]}""",
            """{"somearray":["three","one","two"],"added":true}""",
            "Staging",
            "Production",
            "somearray",
            Context());

        await vm.ApplyArrayMatchAsync("$.somearray", ArrayMatchMode.Unordered);
        await vm.IgnorePathCommand.ExecuteAsync("$.added");

        var dialog = new DiffPreviewDialog(vm);
        dialog.Show();
        dialog.UpdateLayout();
        return dialog;
    }

    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>();

    [AvaloniaFact]
    public void The_dialog_can_be_constructed()
    {
        // Deliberately asserts nothing else - see CollectionRunTests for why a bare construction test
        // earns its place in this codebase.
        var dialog = new DiffPreviewDialog(new DiffPreviewViewModel(Comparison()));

        Assert.NotNull(dialog);
    }

    [AvaloniaFact]
    public async Task The_whitespace_toggle_is_bound_to_the_setting()
    {
        var dialog = await OpenAsync();
        var vm = (DiffPreviewViewModel)dialog.DataContext!;

        var toggle = Assert.Single(
            Descendants<ToggleButton>(dialog), t => Equals(t.Content, "Whitespace"));

        Assert.False(vm.IgnoreWhitespace);
        toggle.IsChecked = true;
        Assert.True(vm.IgnoreWhitespace);
    }

    /// <summary>
    /// The chip's ✕ reaches the view model through <c>$parent[Window]</c>, which is the kind of path
    /// that compiles whatever it says and resolves to nothing when it is wrong.
    /// </summary>
    [AvaloniaFact]
    public async Task Every_rule_chip_has_a_working_remove_button()
    {
        var dialog = await OpenAsync();

        var crosses = Descendants<Button>(dialog).Where(b => Equals(b.Content, "✕")).ToList();

        // One for the ignored path, one for the array rule.
        Assert.Equal(2, crosses.Count);
        Assert.All(crosses, b => Assert.NotNull(b.Command));
        Assert.All(crosses, b => Assert.NotNull(b.CommandParameter));
    }

    [AvaloniaFact]
    public async Task The_array_chip_removes_the_rule_when_pressed()
    {
        var dialog = await OpenAsync();
        var vm = (DiffPreviewViewModel)dialog.DataContext!;

        var cross = Assert.Single(
            Descendants<Button>(dialog),
            b => Equals(b.Content, "✕") && Equals(b.CommandParameter, "$.somearray"));

        cross.Command!.Execute(cross.CommandParameter);

        Assert.Empty(vm.ArrayRules);
    }
}
