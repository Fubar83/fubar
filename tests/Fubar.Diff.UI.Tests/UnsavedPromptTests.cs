using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// The two prompts that stand between the user and losing work: one when a file changes on disk under
/// unsaved changes, and one when a tab or the window is closed with changes still in it.
///
/// The tests that matter here are the REFUSALS. A prompt that cannot be shown, or one the user
/// dismissed, must never be read as agreement - that is the whole reason these exist.
/// </summary>
public class UnsavedPromptTests
{
    private sealed class Files : ITextFileReader, ITextFileWriter
    {
        private readonly Dictionary<string, string[]> _files;

        public Files(params (string Path, string[] Lines)[] files) =>
            _files = files.ToDictionary(f => f.Path, f => f.Lines, StringComparer.Ordinal);

        public IReadOnlyList<string>? Written { get; private set; }

        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            _files.TryGetValue(path, out var lines)
                ? Task.FromResult(new TextDocument(path, lines, TextFormat.Default))
                : throw new TextFileReadException(path, "the file does not exist.");

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default)
        {
            Written = lines;
            _files[path] = [.. lines];

            return Task.CompletedTask;
        }

        /// <summary>Changes a file behind the app's back, as another program would.</summary>
        public void ChangeOnDisk(string path, params string[] lines) => _files[path] = lines;
    }

    /// <summary>Answers with a fixed choice, and records what it was asked.</summary>
    private sealed class Prompt(int answer) : IConfirmationService
    {
        public int Asked { get; private set; }

        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public IReadOnlyList<string>? LastChoices { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) =>
            Task.FromResult(answer == 0);

        public Task<string?> AskForTextAsync(string title, string message, string initial = "") =>
            throw new NotSupportedException("these tests never ask for text");

        public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices)
        {
            Asked++;
            LastTitle = title;
            LastMessage = message;
            LastChoices = choices;

            return Task.FromResult(answer);
        }
    }

    private sealed class Watcher : IFileChangeWatcher
    {
        public event EventHandler? Changed;

        public void Watch(IReadOnlyList<string> paths) { }

        public void Stop() { }

        public void Dispose() { }

        public void Raise()
        {
            Changed?.Invoke(this, EventArgs.Empty);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickFilesAsync(string title) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class NoClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private static (ComparisonViewModel Tab, Files Disk, Watcher Watcher) Build(IConfirmationService? prompt)
    {
        var disk = new Files(("left.txt", ["a", "LEFT", "c"]), ("right.txt", ["a", "RIGHT", "c"]));
        var watcher = new Watcher();

        var comparisons = new FileComparisonService(
            disk,
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        var tab = new ComparisonViewModel(
            comparisons,
            new MergeService(disk),
            new NoPicker(),
            watcher,
            new NoClipboard(),
            disk,
            new ThemeManagerViewModel(),
            prompt)
        {
            LeftPath = "left.txt",
            RightPath = "right.txt",
        };

        return (tab, disk, watcher);
    }

    /// <summary>Compares, then leaves the right-hand file with an unsaved change.</summary>
    private static async Task<ComparisonViewModel> Dirty(ComparisonViewModel tab)
    {
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeLeftCommand.ExecuteAsync(null);

        return tab;
    }

    // ---- Closing ----------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task A_clean_tab_closes_without_asking()
    {
        // The prompt is for the case where something would actually be lost, not a ceremony on the
        // way out of every comparison.
        var prompt = new Prompt(0);
        var (tab, _, _) = Build(prompt);
        await tab.CompareAsync();

        Assert.True(await tab.ConfirmDiscardAsync());
        Assert.Equal(0, prompt.Asked);
    }

    [AvaloniaFact]
    public async Task Closing_with_unsaved_changes_asks_first()
    {
        var prompt = new Prompt(1);
        var (tab, _, _) = Build(prompt);
        await Dirty(tab);

        Assert.True(await tab.ConfirmDiscardAsync());

        Assert.Equal(1, prompt.Asked);
        Assert.Contains("right.txt", prompt.LastMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Choosing_save_writes_the_file_before_closing()
    {
        var prompt = new Prompt(0);
        var (tab, disk, _) = Build(prompt);
        await Dirty(tab);

        Assert.True(await tab.ConfirmDiscardAsync());

        Assert.Equal(["a", "LEFT", "c"], disk.Written);
        Assert.False(tab.HasUnsavedEdits);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_prompt_refuses_the_close()
    {
        // "Went away" is not agreement to discard.
        var prompt = new Prompt(-1);
        var (tab, disk, _) = Build(prompt);
        await Dirty(tab);

        Assert.False(await tab.ConfirmDiscardAsync());
        Assert.Null(disk.Written);
        Assert.True(tab.HasUnsavedEdits);
    }

    [AvaloniaFact]
    public async Task With_no_way_to_ask_the_close_is_refused()
    {
        // A prompt that cannot be shown must not silently discard the work it was going to ask about.
        var (tab, _, _) = Build(prompt: null);
        await Dirty(tab);

        Assert.False(await tab.ConfirmDiscardAsync());
    }

    // ---- Changed on disk --------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task A_clean_comparison_reloads_silently()
    {
        // The existing behaviour, and worth keeping: a diff kept open beside an editor stays current
        // without asking permission every time something is saved.
        var prompt = new Prompt(0);
        var (tab, disk, watcher) = Build(prompt);
        await tab.CompareAsync();

        disk.ChangeOnDisk("right.txt", "a", "NEWER", "c");
        watcher.Raise();

        Assert.Equal(0, prompt.Asked);
        Assert.False(tab.FilesChangedOnDisk);
    }

    [AvaloniaFact]
    public async Task A_change_under_unsaved_edits_asks_what_to_do()
    {
        var prompt = new Prompt(-1);
        var (tab, disk, watcher) = Build(prompt);
        await Dirty(tab);

        disk.ChangeOnDisk("right.txt", "a", "NEWER", "c");
        watcher.Raise();

        Assert.Equal(1, prompt.Asked);
        Assert.Contains("changed on disk", prompt.LastTitle!, StringComparison.OrdinalIgnoreCase);

        // Keeping the user's work is first, because it is the answer a dismissed dialog gives and the
        // only one of the three that cannot lose anything.
        Assert.StartsWith("Keep", prompt.LastChoices![0], StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Dismissing_the_conflict_keeps_the_users_changes()
    {
        var prompt = new Prompt(-1);
        var (tab, _, watcher) = Build(prompt);
        await Dirty(tab);

        watcher.Raise();

        Assert.True(tab.HasUnsavedEdits);

        // And the banner stays up, so the situation is not left unmarked just because a dialog closed.
        Assert.True(tab.FilesChangedOnDisk);
    }

    [AvaloniaFact]
    public async Task Choosing_to_save_writes_over_what_changed()
    {
        var prompt = new Prompt(1);
        var (tab, disk, watcher) = Build(prompt);
        await Dirty(tab);

        disk.ChangeOnDisk("right.txt", "a", "NEWER", "c");
        watcher.Raise();

        Assert.Equal(["a", "LEFT", "c"], disk.Written);
        Assert.False(tab.HasUnsavedEdits);
    }

    [AvaloniaFact]
    public async Task Choosing_to_reload_discards_them()
    {
        var prompt = new Prompt(2);
        var (tab, disk, watcher) = Build(prompt);
        await Dirty(tab);

        disk.ChangeOnDisk("right.txt", "a", "NEWER", "c");
        watcher.Raise();

        Assert.False(tab.HasUnsavedEdits);
        Assert.Null(disk.Written);
    }

    [AvaloniaFact]
    public async Task With_auto_refresh_off_the_change_is_reported_rather_than_ignored()
    {
        // This used to do nothing at all - the file changed and the user went on reading a stale
        // comparison with no sign of it. Saying so is not the same as reloading behind their back.
        var prompt = new Prompt(-1);
        var (tab, disk, watcher) = Build(prompt);
        await tab.CompareAsync();

        tab.AutoRefresh = false;

        disk.ChangeOnDisk("right.txt", "a", "NEWER", "c");
        watcher.Raise();

        Assert.True(tab.FilesChangedOnDisk);
        Assert.Equal(0, prompt.Asked);
    }

    [AvaloniaFact]
    public async Task Our_own_save_never_raises_a_conflict()
    {
        // Saving re-reads the file deliberately; treating that as an external change would prompt the
        // user about their own save.
        var prompt = new Prompt(-1);
        var (tab, _, watcher) = Build(prompt);
        await Dirty(tab);

        await tab.SaveCommand.ExecuteAsync(null);
        watcher.Raise();

        Assert.Equal(0, prompt.Asked);
        Assert.False(tab.FilesChangedOnDisk);
    }
}
