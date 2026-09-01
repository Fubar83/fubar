using Avalonia.Headless.XUnit;
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
/// Aligning two lines by hand, from the window's side of it.
///
/// The engine's half is pinned in Fubar.Diff.Infrastructure.Tests; what matters here is the part a
/// user actually touches - that it reads both carets, that it refuses politely when there is nothing
/// to pair, and that a pairing does not survive the files being replaced, since "line 40 here is
/// line 62 there" means nothing about a different pair of files.
/// </summary>
public class ManualAlignmentTests
{
    private sealed class Files(Dictionary<string, string[]> files) : ITextFileReader, ITextFileWriter
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextDocument(path, files[path], TextFormat.Default));

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default)
        {
            files[path] = [.. lines];

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

    private static ComparisonViewModel Build()
    {
        var disk = new Files(new()
        {
            ["left.txt"] = ["one", "two", "three", "four"],
            ["right.txt"] = ["ONE", "TWO", "THREE", "FOUR"],
            ["other.txt"] = ["completely", "different"],
        });

        var comparisons = new FileComparisonService(
            disk,
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        return new ComparisonViewModel(
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
    }

    /// <summary>Stands in for the two panes' carets, which is all the command reads.</summary>
    private static void PutCarets(ComparisonViewModel tab, int? left, int? right) =>
        tab.Pane.CaretLineReader = side => side == DiffSide.Left ? left : right;

    [AvaloniaFact]
    public async Task Aligning_two_carets_records_the_pairing()
    {
        var tab = Build();
        await tab.CompareAsync();

        PutCarets(tab, left: 2, right: 3);
        await tab.AlignCaretsCommand.ExecuteAsync(null);

        Assert.True(tab.HasAlignments);
        Assert.Equal(new AlignmentAnchor(2, 3), Assert.Single(tab.Alignments));
    }

    [AvaloniaFact]
    public async Task The_comparison_honours_it()
    {
        // The whole point: the rows that come back pair those two lines, whatever the aligner would
        // have done on its own.
        var tab = Build();
        await tab.CompareAsync();

        PutCarets(tab, left: 2, right: 3);
        await tab.AlignCaretsCommand.ExecuteAsync(null);

        var row = tab.Pane.Result.Lines.Single(r => r.LeftNumber == 2);
        Assert.Equal(3, row.RightNumber);
    }

    [AvaloniaFact]
    public async Task A_caret_on_a_filler_is_refused_with_a_reason()
    {
        // Null is what a pane reports for a filler row: there is no line there to pair with, and
        // silently doing nothing would look like a broken shortcut.
        var tab = Build();
        await tab.CompareAsync();

        PutCarets(tab, left: 2, right: null);
        await tab.AlignCaretsCommand.ExecuteAsync(null);

        Assert.False(tab.HasAlignments);
        Assert.Contains("caret", tab.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Clearing_puts_the_aligner_back_in_charge()
    {
        var tab = Build();
        await tab.CompareAsync();

        PutCarets(tab, left: 2, right: 3);
        await tab.AlignCaretsCommand.ExecuteAsync(null);
        await tab.ClearAlignmentsCommand.ExecuteAsync(null);

        Assert.False(tab.HasAlignments);
        Assert.Equal(2, tab.Pane.Result.Lines.Single(r => r.LeftNumber == 2).RightNumber);
    }

    [AvaloniaFact]
    public async Task Replacing_a_file_drops_the_pairings()
    {
        // They were about the file that just left. An anchor that happened to fall inside the new one
        // would be honoured while meaning nothing at all.
        var tab = Build();
        await tab.CompareAsync();

        PutCarets(tab, left: 2, right: 3);
        await tab.AlignCaretsCommand.ExecuteAsync(null);
        Assert.True(tab.HasAlignments);

        await tab.OpenFilesAsync(["left.txt", "other.txt"]);

        Assert.False(tab.HasAlignments);
    }

    [AvaloniaFact]
    public async Task The_count_is_what_the_status_bar_shows()
    {
        var tab = Build();
        await tab.CompareAsync();

        PutCarets(tab, left: 1, right: 1);
        await tab.AlignCaretsCommand.ExecuteAsync(null);

        PutCarets(tab, left: 3, right: 4);
        await tab.AlignCaretsCommand.ExecuteAsync(null);

        Assert.Equal(2, tab.AlignmentCount);
    }
}
