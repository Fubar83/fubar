using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Diff.Application.Folders;
using Fubar.Diff.Core.Folders;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;
using Fubar.Diff.UI.Views;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// That the folder window's markup parses and binds.
///
/// This is the cheapest test in the suite and one of the more valuable: a XAML mistake - a mistyped
/// property, a template that will not resolve, a style with the wrong data type - throws when the
/// window is constructed, and the only other way to find out is to click the button that opens it.
/// A binding path typo does not throw, so the item count is asserted too.
/// </summary>
public class FolderWindowTests
{
    private sealed class StubService(FolderComparison result) : IFolderComparisonService
    {
        public Task<FolderComparison> CompareAsync(
            string leftRoot, string rightRoot, FolderComparisonOptions options,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task<FolderViewModel> Populated()
    {
        FolderEntry File(string name, FolderEntryStatus status) =>
            new(name, name, false, status, 10, 20, []) { LeftRelativePath = name, RightRelativePath = name };

        var entries = new FolderEntry[]
        {
            new("src", "src", true, FolderEntryStatus.Different, FolderEntry.NoSize, FolderEntry.NoSize,
                [File("app.cs", FolderEntryStatus.Different)]),
            File("only-left.txt", FolderEntryStatus.LeftOnly),
        };

        var folders = new FolderViewModel(
            new StubService(FolderComparison.Create(@"C:\left", @"C:\right", entries)),
            new NoPicker(),
            new ThemeManagerViewModel())
        {
            LeftPath = @"C:\left",
            RightPath = @"C:\right",
        };

        await folders.CompareAsync();

        return folders;
    }

    [AvaloniaFact]
    public async Task The_window_builds_and_shows_the_tree()
    {
        var folders = await Populated();

        var window = new FolderWindow { DataContext = folders };
        window.Show();
        window.UpdateLayout();

        var tree = window.GetVisualDescendants().OfType<TreeView>().Single();

        Assert.Same(folders.Entries, tree.ItemsSource);
        Assert.Equal(2, folders.Entries.Count);
    }

    [AvaloniaFact]
    public void An_empty_window_builds_too()
    {
        // The state it opens in, before any folder is chosen.
        var folders = new FolderViewModel(
            new StubService(FolderComparison.Empty), new NoPicker(), new ThemeManagerViewModel());

        var window = new FolderWindow { DataContext = folders };
        window.Show();
        window.UpdateLayout();

        Assert.False(folders.HasResults);
    }

    [AvaloniaFact]
    public async Task Rows_are_rendered_for_every_visible_entry()
    {
        // Proves the tree template resolved rather than leaving empty containers, which is what a
        // broken TreeDataTemplate looks like.
        var folders = await Populated();

        var window = new FolderWindow { DataContext = folders };
        window.Show();
        window.UpdateLayout();

        var texts = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .ToList();

        Assert.Contains("src", texts);
        Assert.Contains("only-left.txt", texts);
        Assert.Contains("left only", texts);
    }
}
