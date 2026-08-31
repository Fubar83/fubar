using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// Closing tabs and the window when something is unsaved.
///
/// The tab already knows how to ask (see <see cref="UnsavedPromptTests"/>); what these check is that
/// the shell actually ASKS before tearing anything down, and takes no for an answer. A close that
/// ignores the refusal loses exactly the work the prompt exists to protect.
/// </summary>
public class ShellCloseTests
{
    private sealed class Comparisons : IFileComparisonService
    {
        public Task<FileComparison> CompareFilesAsync(
            string leftPath, string rightPath, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileComparison(
                new TextDocument(leftPath, ["a"], TextFormat.Default),
                new TextDocument(rightPath, ["b"], TextFormat.Default),
                options,
                DiffResult.Create([new DiffLine(1, "a", 1, "b", ChangeKind.Modified)])));

        public Task<FileComparison> CompareTextAsync(
            string leftText, string rightText, ComparisonOptions options,
            string leftLabel = "left", string rightLabel = "right", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileComparison> CompareDocumentsAsync(
            TextDocument left, TextDocument right, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileComparison(left, right, options, DiffResult.Empty));

        public Task<FileComparison> RecompareAsync(
            FileComparison comparison, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(comparison);

        public JsonDisplay FormatJsonForDisplay(
            FileComparison comparison, bool prettyLeft, bool prettyRight, Fubar.Diff.Core.Json.JsonFormatOptions format) =>
            new(comparison.OriginalLeftText, comparison.OriginalRightText, comparison.OriginalSemanticChanges);

        public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) => comparison;
    }

    private sealed class Merges : IMergeService
    {
        public int Saves { get; private set; }

        public Task<string> SaveAsync(
            FileComparison comparison, MergeState state, DiffSide baseSide,
            string? targetPath = null, CancellationToken cancellationToken = default)
        {
            Saves++;

            return Task.FromResult("saved.txt");
        }

        public string Preview(FileComparison comparison, MergeState state, DiffSide baseSide) => string.Empty;

        public Task<string> SaveThreeWayAsync(
            ThreeWayComparison comparison, ThreeWayMergeState state, MergeSide destination,
            string? targetPath = null, CancellationToken cancellationToken = default) => Task.FromResult("x");

        public string PreviewThreeWay(ThreeWayComparison c, ThreeWayMergeState s, MergeSide d) => string.Empty;
    }

    private sealed class Prompt(int answer) : IConfirmationService
    {
        public int Asked { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) =>
            Task.FromResult(answer == 0);

        public Task<string?> AskForTextAsync(string title, string message, string initial = "") =>
            throw new NotSupportedException("these tests never ask for text");

        public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices)
        {
            Asked++;

            return Task.FromResult(answer);
        }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class NoWatcher : IFileChangeWatcher
    {
        public event EventHandler? Changed;

        public void Watch(IReadOnlyList<string> paths) => _ = Changed;

        public void Stop() { }

        public void Dispose() { }
    }

    private sealed class NoClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class NoWriter : ITextFileWriter
    {
        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => AppSettings.Default;

        public Task<bool> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static (ShellViewModel Shell, Prompt Prompt, Merges Merge) Build(int answer)
    {
        var prompt = new Prompt(answer);
        var merges = new Merges();
        var theme = new ThemeManagerViewModel();

        ComparisonViewModel NewTab() => new(
            new Comparisons(), merges, new NoPicker(), new NoWatcher(),
            new NoClipboard(), new NoWriter(), theme, prompt);

        var shell = new ShellViewModel(
            NewTab,
            () => throw new NotSupportedException(),
            () => throw new NotSupportedException(),
            new Settings(),
            theme);

        return (shell, prompt, merges);
    }

    /// <summary>Opens a tab and leaves its right-hand file with an unsaved change.</summary>
    private static async Task<ComparisonViewModel> DirtyTab(ShellViewModel shell)
    {
        var tab = shell.AddTab();
        tab.LeftPath = "left.txt";
        tab.RightPath = "right.txt";

        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeLeftCommand.ExecuteAsync(null);

        Assert.True(tab.HasUnsavedEdits);

        return tab;
    }

    [AvaloniaFact]
    public async Task Closing_a_clean_tab_asks_nothing()
    {
        var (shell, prompt, _) = Build(answer: 0);

        var first = shell.AddTab();
        shell.AddTab();

        await shell.CloseTabCommand.ExecuteAsync(first);

        Assert.Equal(0, prompt.Asked);
        Assert.DoesNotContain(first, shell.Tabs);
    }

    [AvaloniaFact]
    public async Task Closing_a_tab_with_unsaved_changes_asks_first()
    {
        var (shell, prompt, _) = Build(answer: 1);

        var tab = await DirtyTab(shell);
        shell.AddTab();

        await shell.CloseTabCommand.ExecuteAsync(tab);

        Assert.Equal(1, prompt.Asked);
        Assert.DoesNotContain(tab, shell.Tabs);
    }

    [AvaloniaFact]
    public async Task Cancelling_keeps_the_tab_open()
    {
        // The refusal that matters: the tab is still there, still holding what the user typed.
        var (shell, _, _) = Build(answer: -1);

        var tab = await DirtyTab(shell);
        shell.AddTab();

        await shell.CloseTabCommand.ExecuteAsync(tab);

        Assert.Contains(tab, shell.Tabs);
        Assert.True(tab.HasUnsavedEdits);
    }

    [AvaloniaFact]
    public async Task Choosing_save_writes_before_the_tab_goes()
    {
        var (shell, _, merges) = Build(answer: 0);

        var tab = await DirtyTab(shell);
        shell.AddTab();

        await shell.CloseTabCommand.ExecuteAsync(tab);

        Assert.Equal(1, merges.Saves);
        Assert.DoesNotContain(tab, shell.Tabs);
    }

    // ---- Closing the window -----------------------------------------------------------------------

    [AvaloniaFact]
    public async Task The_window_closes_when_every_tab_is_clean()
    {
        var (shell, prompt, _) = Build(answer: 0);
        shell.AddTab();

        Assert.True(await shell.ConfirmCloseAsync());
        Assert.Equal(0, prompt.Asked);
    }

    [AvaloniaFact]
    public async Task One_tab_refusing_stops_the_window_closing()
    {
        var (shell, _, _) = Build(answer: -1);

        shell.AddTab();
        var dirty = await DirtyTab(shell);

        Assert.False(await shell.ConfirmCloseAsync());

        // And the tab in question is the one on screen, so the user is looking at what they were just
        // asked about rather than at whichever tab happened to be in front.
        Assert.Same(dirty, shell.SelectedTab);
    }

    [AvaloniaFact]
    public async Task Every_dirty_tab_is_asked_about()
    {
        var (shell, prompt, _) = Build(answer: 1);

        await DirtyTab(shell);
        await DirtyTab(shell);

        Assert.True(await shell.ConfirmCloseAsync());
        Assert.Equal(2, prompt.Asked);
    }
}
