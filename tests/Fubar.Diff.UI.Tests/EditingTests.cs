using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// Editing a comparison, through the REAL aligner rather than a stub - because the thing worth
/// checking is what the files end up containing, and a stub that returns a fixed result cannot answer
/// that.
///
/// The behaviour change to protect is that taking a side is now an EDIT. It rewrites the document
/// there and then instead of recording a decision to be applied at save time, which is what makes it
/// visible, undoable, and immune to being renumbered by the next comparison.
/// </summary>
public class EditingTests
{
    /// <summary>Serves two in-memory files and records what was written back.</summary>
    private sealed class Files : ITextFileReader, ITextFileWriter
    {
        private readonly Dictionary<string, string[]> _files;

        public Files(params (string Path, string[] Lines)[] files) =>
            _files = files.ToDictionary(f => f.Path, f => f.Lines, StringComparer.Ordinal);

        public IReadOnlyList<string>? Written { get; private set; }

        public string? WrittenTo { get; private set; }

        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            _files.TryGetValue(path, out var lines)
                ? Task.FromResult(new TextDocument(path, lines, TextFormat.Default))
                : throw new TextFileReadException(path, "the file does not exist.");

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default)
        {
            WrittenTo = path;
            Written = lines;
            _files[path] = [.. lines];

            return Task.CompletedTask;
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

    private static (ComparisonViewModel Tab, Files Disk) Build(string[] left, string[] right)
    {
        var disk = new Files(("left.txt", left), ("right.txt", right));

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
            new NoWatcher(),
            new NoClipboard(),
            disk,
            new ThemeManagerViewModel())
        {
            LeftPath = "left.txt",
            RightPath = "right.txt",
        };

        return (tab, disk);
    }

    [AvaloniaFact]
    public async Task Taking_the_left_side_rewrites_the_right_document_immediately()
    {
        // The heart of the change. It used to be invisible until save; now the right-hand file simply
        // contains the left's version, and the difference is gone.
        var (tab, _) = Build(["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeLeftCommand.ExecuteAsync(null);

        Assert.False(tab.Pane.HasChanges);
        Assert.True(tab.HasUnsavedEdits);
    }

    [AvaloniaFact]
    public async Task Taking_the_right_side_rewrites_the_LEFT_document()
    {
        var (tab, _) = Build(["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeRightCommand.ExecuteAsync(null);

        Assert.False(tab.Pane.HasChanges);
    }

    [AvaloniaFact]
    public async Task Resolving_every_change_leaves_nothing_to_show()
    {
        var (tab, _) = Build(["a", "LEFT", "c", "gone"], ["a", "RIGHT", "c"]);
        await tab.CompareAsync();

        // Back to front, because resolving one renumbers the rest - which is exactly the renumbering
        // that used to silently reassign pending decisions, and now cannot, because there are none.
        for (var i = tab.Pane.Hunks.Count - 1; i >= 0; i--)
        {
            tab.Pane.CurrentHunk = i;
            await tab.TakeLeftCommand.ExecuteAsync(null);
        }

        Assert.False(tab.Pane.HasChanges);
    }

    [AvaloniaFact]
    public async Task Saving_writes_what_the_pane_now_holds()
    {
        var (tab, disk) = Build(["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeLeftCommand.ExecuteAsync(null);
        await tab.SaveCommand.ExecuteAsync(null);

        Assert.Equal("right.txt", disk.WrittenTo);
        Assert.Equal(["a", "LEFT", "c"], disk.Written);
        Assert.False(tab.HasUnsavedEdits);
    }

    [AvaloniaFact]
    public async Task An_insertion_taken_from_the_left_is_added_rather_than_blanked()
    {
        // The classic way to get a merge wrong: leaving an empty line where the other side had none.
        var (tab, disk) = Build(["a", "extra", "c"], ["a", "c"]);
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeLeftCommand.ExecuteAsync(null);
        await tab.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["a", "extra", "c"], disk.Written);
    }

    [AvaloniaFact]
    public async Task A_deletion_taken_from_the_left_removes_the_line()
    {
        var (tab, disk) = Build(["a", "c"], ["a", "unwanted", "c"]);
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeLeftCommand.ExecuteAsync(null);
        await tab.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["a", "c"], disk.Written);
    }

    [AvaloniaFact]
    public async Task Saving_with_no_changes_writes_nothing_at_all()
    {
        // Ctrl+S on an untouched comparison used to rewrite the right-hand file with its own content.
        // Now that both sides are tracked separately, "save" means "save what changed" - and rewriting
        // a file nobody edited moves its timestamp, which is enough to make a build think it is stale.
        var (tab, disk) = Build(["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        await tab.CompareAsync();

        await tab.SaveCommand.ExecuteAsync(null);

        Assert.Null(disk.Written);
    }

    [AvaloniaFact]
    public async Task Each_side_is_tracked_and_saved_on_its_own()
    {
        // Both panes are editable, so a session can leave two files to write - and saving one of them
        // is not "saved".
        var (tab, disk) = Build(["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeLeftCommand.ExecuteAsync(null);

        Assert.True(tab.HasUnsavedRight);
        Assert.False(tab.HasUnsavedLeft);

        await tab.SaveRightCommand.ExecuteAsync(null);

        Assert.False(tab.HasUnsavedRight);
        Assert.Equal("right.txt", disk.WrittenTo);
    }

    [AvaloniaFact]
    public async Task Taking_the_right_side_leaves_the_LEFT_file_to_save()
    {
        var (tab, disk) = Build(["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        await tab.TakeRightCommand.ExecuteAsync(null);

        Assert.True(tab.HasUnsavedLeft);
        Assert.False(tab.HasUnsavedRight);

        await tab.SaveCommand.ExecuteAsync(null);

        Assert.Equal("left.txt", disk.WrittenTo);
        Assert.Equal(["a", "RIGHT", "c"], disk.Written);
    }

    [AvaloniaFact]
    public async Task The_unsaved_description_names_the_files_that_would_be_lost()
    {
        // What the prompts show. "You have unsaved changes" is not enough to decide on - which file?
        var (tab, _) = Build(["a", "LEFT", "c"], ["a", "RIGHT", "c"]);
        await tab.CompareAsync();

        Assert.Empty(tab.UnsavedDescription);

        tab.Pane.CurrentHunk = 0;
        await tab.TakeLeftCommand.ExecuteAsync(null);

        Assert.Contains("right.txt", tab.UnsavedDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("left.txt", tab.UnsavedDescription, StringComparison.Ordinal);
    }

    // ---- The toggle -------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Editing_is_off_until_asked_for()
    {
        var (tab, _) = Build(["a"], ["b"]);
        await tab.CompareAsync();

        Assert.False(tab.IsEditing);
        Assert.False(tab.Pane.IsEditable);
        Assert.False(AppSettings.Default.Editing);
    }

    [AvaloniaFact]
    public async Task Turning_it_on_makes_the_panes_editable()
    {
        var (tab, _) = Build(["a"], ["b"]);
        await tab.CompareAsync();

        tab.IsEditing = true;

        Assert.True(tab.Pane.IsEditable);
    }

    [AvaloniaFact]
    public async Task It_is_offered_for_a_text_comparison_shown_side_by_side()
    {
        var (tab, _) = Build(["a"], ["b"]);
        await tab.CompareAsync();

        Assert.True(tab.CanEdit);
    }

    [AvaloniaFact]
    public async Task It_is_not_offered_in_the_unified_view()
    {
        // The unified document has its own row coordinates - a modified row becomes two lines there -
        // so there is no single place an edit belongs.
        var (tab, _) = Build(["a"], ["b"]);
        await tab.CompareAsync();

        tab.Pane.ViewMode = DiffViewMode.Unified;

        Assert.False(tab.CanEdit);
    }

    [AvaloniaFact]
    public void The_setting_is_persisted_and_restored()
    {
        var (tab, _) = Build(["a"], ["b"]);

        tab.ApplyDefaults(AppSettings.Default with { Editing = true });

        Assert.True(tab.IsEditing);
        Assert.True(tab.CaptureOptions(AppSettings.Default).Editing);
    }
}
