using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// A tab showing a comparison of files that are not text.
///
/// The one that MATTERS here is the merge guard. The pane is handed hex rows, which are an ordinary
/// <c>DiffResult</c> as far as everything downstream can tell - including the merge, which would
/// happily write those hex strings over the user's PNG. The rest of these check that the view is
/// steered to the right thing; that one checks the app cannot destroy a file.
/// </summary>
public class BinaryComparisonTabTests
{
    private sealed class StubComparison(BinaryComparison? binary) : IFileComparisonService
    {
        public Task<FileComparison> CompareFilesAsync(
            string leftPath, string rightPath, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileComparison(
                new TextDocument(leftPath, binary is null ? ["a"] : [], TextFormat.Default),
                new TextDocument(rightPath, binary is null ? ["b"] : [], TextFormat.Default),
                options,
                binary is null
                    ? DiffResult.Create([new DiffLine(1, "a", 1, "b", ChangeKind.Modified)])
                    : DiffResult.Empty)
            {
                Binary = binary,
            });

        public Task<FileComparison> CompareTextAsync(
            string leftText, string rightText, ComparisonOptions options,
            string leftLabel = "left", string rightLabel = "right", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileComparison> CompareDocumentsAsync(
            TextDocument left, TextDocument right, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileComparison(left, right, options, DiffResult.Empty) { Binary = binary });

        public Task<FileComparison> RecompareAsync(
            FileComparison comparison, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(comparison);

        public JsonDisplay FormatJsonForDisplay(
            FileComparison comparison, bool prettyLeft, bool prettyRight, Fubar.Diff.Core.Json.JsonFormatOptions format) =>
            new(comparison.OriginalLeftText, comparison.OriginalRightText, comparison.OriginalSemanticChanges);

        public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) => comparison;
    }

    /// <summary>Records every merge write that was attempted. There must never be one for binary.</summary>
    private sealed class RecordingMerge : IMergeService
    {
        public int Saves { get; private set; }

        public Task<string> SaveAsync(
            FileComparison comparison, MergeState state, DiffSide baseSide,
            string? targetPath = null, CancellationToken cancellationToken = default)
        {
            Saves++;

            return Task.FromResult(targetPath ?? "right.bin");
        }

        public string Preview(FileComparison comparison, MergeState state, DiffSide baseSide) => string.Empty;

        public Task<string> SaveThreeWayAsync(
            ThreeWayComparison comparison, ThreeWayMergeState state, MergeSide destination,
            string? targetPath = null, CancellationToken cancellationToken = default) => Task.FromResult("x");

        public Task<string> SaveThreeWayTextAsync(
            ThreeWayComparison comparison, MergeSide destination, IReadOnlyList<string> lines,
            string? targetPath = null, CancellationToken cancellationToken = default) => Task.FromResult("x");

        public string PreviewThreeWay(ThreeWayComparison c, ThreeWayMergeState s, MergeSide d) => string.Empty;
    }

    private sealed class Picker(string? save = null) : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickFilesAsync(string title) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult(save);

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

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static BinaryComparison Bytes(byte[] left, byte[] right) =>
        BinaryComparer.Compare(
            new BinaryDocument("l", left, ImageFormatDetector.Detect(left)),
            new BinaryDocument("r", right, ImageFormatDetector.Detect(right)));

    private static (ComparisonViewModel Tab, RecordingMerge Merge) Build(BinaryComparison? binary, string? savePath = null)
    {
        var merge = new RecordingMerge();

        var tab = new ComparisonViewModel(
            new StubComparison(binary), merge, new Picker(savePath), new NoWatcher(),
            new NoClipboard(), new NoWriter(), new ThemeManagerViewModel())
        {
            LeftPath = "left.bin",
            RightPath = "right.bin",
        };

        return (tab, merge);
    }

    [AvaloniaFact]
    public async Task The_tab_knows_it_is_showing_bytes()
    {
        var (tab, _) = Build(Bytes([1, 2, 3], [1, 9, 3]));
        await tab.CompareAsync();

        Assert.True(tab.IsBinaryComparison);
        Assert.NotNull(tab.Binary);
    }

    [AvaloniaFact]
    public async Task The_pane_is_given_hex_rows_so_every_existing_view_works_on_them()
    {
        var (tab, _) = Build(Bytes(new byte[48], MutatedAt(20)));
        await tab.CompareAsync();

        Assert.Equal(3, tab.Pane.TotalLines);
        Assert.True(tab.Pane.HasChanges);

        // Hex, not the file's own text: the offset column is what makes a byte comparison readable.
        Assert.StartsWith("00000000  ", tab.Pane.LeftDocument!.Text, StringComparison.Ordinal);
    }

    private static byte[] MutatedAt(int index)
    {
        var bytes = new byte[48];
        bytes[index] = 0xFF;

        return bytes;
    }

    [AvaloniaFact]
    public async Task Saving_a_binary_comparison_writes_NOTHING()
    {
        // The guard the whole feature rests on. A merge over a binary comparison would build a document
        // from its EMPTY text documents and write it over the user's file.
        var (tab, merge) = Build(Bytes([1, 2, 3], [1, 9, 3]));
        await tab.CompareAsync();

        await tab.SaveCommand.ExecuteAsync(null);
        await tab.SaveLeftCommand.ExecuteAsync(null);
        await tab.SaveRightCommand.ExecuteAsync(null);
        await tab.SaveLeftAsCommand.ExecuteAsync(null);

        Assert.Equal(0, merge.Saves);
        Assert.False(tab.CanMerge);
    }

    [AvaloniaFact]
    public async Task A_text_comparison_still_saves()
    {
        // The other half of the guard: it must not have switched merging off for everyone.
        var (tab, merge) = Build(binary: null);
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        tab.TakeLeftCommand.Execute(null);
        await tab.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, merge.Saves);
        Assert.True(tab.CanMerge);
    }

    [AvaloniaFact]
    public async Task The_merge_controls_are_hidden_even_when_a_hex_hunk_is_selected()
    {
        var (tab, _) = Build(Bytes(new byte[48], MutatedAt(20)));
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;

        Assert.True(tab.Pane.HasCurrentHunk);
        Assert.False(tab.ShowsMergeControls);
    }

    [AvaloniaFact]
    public async Task There_is_no_patch_to_export_from_bytes()
    {
        // A unified diff of hex lines would apply cleanly and produce a text file full of hex.
        var (tab, _) = Build(Bytes(new byte[48], MutatedAt(20)));
        await tab.CompareAsync();

        Assert.False(tab.HasPatch);
    }

    [AvaloniaFact]
    public async Task Two_images_are_offered_as_pictures()
    {
        var (tab, _) = Build(Bytes(Png, [.. Png, 4]));
        await tab.CompareAsync();

        Assert.True(tab.ShowsImages);
    }

    [AvaloniaFact]
    public async Task Bytes_that_are_not_pictures_are_shown_as_hex_alone()
    {
        var (tab, _) = Build(Bytes([0x4D, 0x5A], [0x4D, 0x5B]));
        await tab.CompareAsync();

        Assert.False(tab.ShowsImages);
        Assert.False(tab.Images.HasImages);
    }

    [AvaloniaFact]
    public async Task The_status_line_says_what_a_byte_comparison_can_say()
    {
        var (tab, _) = Build(Bytes([1, 2, 3], [1, 9, 3]));
        await tab.CompareAsync();

        Assert.Contains("Binary", tab.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("0x1", tab.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Identical_binary_files_are_said_to_be_identical()
    {
        var (tab, _) = Build(Bytes([1, 2, 3], [1, 2, 3]));
        await tab.CompareAsync();

        Assert.Contains("identical", tab.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task No_syntax_grammar_is_claimed_for_a_hex_dump()
    {
        // The extension belongs to the file; colouring a dump of its bytes as if it were a PNG - or
        // worse, as C# - would be actively misleading.
        var (tab, _) = Build(Bytes([1, 2, 3], [1, 9, 3]));
        await tab.CompareAsync();

        Assert.Null(tab.Pane.LeftSyntaxExtension);
        Assert.Null(tab.Pane.RightSyntaxExtension);
    }

    [AvaloniaFact]
    public async Task Closing_the_tab_releases_the_decoded_pictures()
    {
        var (tab, _) = Build(Bytes(Png, Png));
        await tab.CompareAsync();

        tab.Dispose();

        Assert.False(tab.Images.HasImages);
    }
}
