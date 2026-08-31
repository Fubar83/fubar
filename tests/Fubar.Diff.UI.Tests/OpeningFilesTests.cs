using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// Choosing what to compare, now that the window has one Open button instead of a picker row.
///
/// The row it replaced could express a half-finished choice by simply showing one empty text box; a
/// button cannot, so the behaviour behind it has to. One file fills the free side and says so, two
/// files compare immediately, and a pair that came back the wrong way round is one Swap away rather
/// than another trip through the dialog.
/// </summary>
public class OpeningFilesTests
{
    private sealed class Files : ITextFileReader, ITextFileWriter
    {
        private readonly Dictionary<string, string[]> _files;

        public Files(params (string Path, string[] Lines)[] files) =>
            _files = files.ToDictionary(f => f.Path, f => f.Lines, StringComparer.Ordinal);

        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            _files.TryGetValue(path, out var lines)
                ? Task.FromResult(new TextDocument(path, lines, TextFormat.Default))
                : throw new TextFileReadException(path, "the file does not exist.");

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default)
        {
            _files[path] = [.. lines];

            return Task.CompletedTask;
        }
    }

    /// <summary>Answers the multi-select dialog with whatever a test says the user chose.</summary>
    private sealed class Picker(params string[] files) : IFilePickerService
    {
        public int Prompts { get; private set; }

        public Task<string?> PickFileAsync(string title)
        {
            Prompts++;

            return Task.FromResult<string?>(files.Length > 0 ? files[0] : null);
        }

        public Task<IReadOnlyList<string>> PickFilesAsync(string title)
        {
            Prompts++;

            return Task.FromResult<IReadOnlyList<string>>(files);
        }

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

    private static ComparisonViewModel Build(IFilePickerService picker)
    {
        var disk = new Files(("left.txt", ["a", "b"]), ("right.txt", ["a", "B"]));

        var comparisons = new FileComparisonService(
            disk,
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        return new ComparisonViewModel(
            comparisons,
            new MergeService(disk),
            picker,
            new NoWatcher(),
            new NoClipboard(),
            disk,
            new ThemeManagerViewModel());
    }

    [AvaloniaFact]
    public async Task Open_takes_both_files_from_one_dialog()
    {
        var picker = new Picker("left.txt", "right.txt");
        var tab = Build(picker);

        await tab.OpenFilesDialogCommand.ExecuteAsync(null);

        Assert.Equal("left.txt", tab.LeftPath);
        Assert.Equal("right.txt", tab.RightPath);
        Assert.True(tab.Pane.HasChanges);

        // One prompt, not two. Two consecutive dialogs is what this replaced.
        Assert.Equal(1, picker.Prompts);
    }

    [AvaloniaFact]
    public async Task Opening_one_file_fills_the_free_side()
    {
        var tab = Build(new Picker("left.txt"));

        await tab.OpenFilesDialogCommand.ExecuteAsync(null);

        Assert.Equal("left.txt", tab.LeftPath);
        Assert.Equal(string.Empty, tab.RightPath);

        // Nothing to compare yet, and the empty state has to say which half is missing rather than
        // asking for two files when one is already chosen.
        Assert.True(tab.HasOneSide);
        Assert.Contains("left.txt", tab.EmptyStateDescription, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_second_single_file_becomes_the_other_side_and_compares()
    {
        var tab = Build(new Picker("left.txt"));
        await tab.OpenFilesDialogCommand.ExecuteAsync(null);

        await tab.OpenFilesAsync(["right.txt"]);

        Assert.Equal("right.txt", tab.RightPath);
        Assert.False(tab.HasOneSide);
        Assert.True(tab.Pane.HasChanges);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_dialog_changes_nothing()
    {
        var tab = Build(new Picker());
        await tab.OpenFilesAsync(["left.txt", "right.txt"]);

        await tab.OpenFilesDialogCommand.ExecuteAsync(null);

        Assert.Equal("left.txt", tab.LeftPath);
        Assert.Equal("right.txt", tab.RightPath);
    }

    [AvaloniaFact]
    public async Task Swapping_exchanges_the_two_sides()
    {
        var tab = Build(new Picker());
        await tab.OpenFilesAsync(["left.txt", "right.txt"]);

        await tab.SwapSidesCommand.ExecuteAsync(null);

        Assert.Equal("right.txt", tab.LeftPath);
        Assert.Equal("left.txt", tab.RightPath);
        Assert.Contains("right.txt", tab.Title, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Swapping_is_refused_over_unsaved_edits()
    {
        // Swapping re-compares, which reloads both sides from disk - and the typed text exists
        // nowhere else.
        var tab = Build(new Picker());
        await tab.OpenFilesAsync(["left.txt", "right.txt"]);

        tab.Pane.FileLinesReader = _ => ["a", "typed"];
        tab.Pane.ReportEdit(DiffSide.Right);

        await tab.SwapSidesCommand.ExecuteAsync(null);

        Assert.Equal("left.txt", tab.LeftPath);
        Assert.Equal("right.txt", tab.RightPath);
    }

    [AvaloniaFact]
    public void Swapping_needs_a_pair()
    {
        var tab = Build(new Picker());

        Assert.False(tab.CanSwapSides);

        tab.LeftPath = "left.txt";
        Assert.False(tab.CanSwapSides);

        tab.RightPath = "right.txt";
        Assert.True(tab.CanSwapSides);
    }
}
