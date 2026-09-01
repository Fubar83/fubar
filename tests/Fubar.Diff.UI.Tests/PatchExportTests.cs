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
/// Exporting the comparison as a patch. The FORMAT is covered in Core; what these check is the wiring -
/// that it uses the current result, names both files, and refuses to produce a patch of nothing.
/// </summary>
public class PatchExportTests
{
    private sealed class StubComparison(DiffResult result) : IFileComparisonService
    {
        public Task<FileComparison> CompareFilesAsync(
            string leftPath, string rightPath, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileComparison(
                new TextDocument(leftPath, ["a"], TextFormat.Default),
                new TextDocument(rightPath, ["b"], TextFormat.Default),
                options,
                result));

        public Task<FileComparison> CompareTextAsync(
            string leftText, string rightText, ComparisonOptions options,
            string leftLabel = "left", string rightLabel = "right", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileComparison> CompareDocumentsAsync(
            TextDocument left, TextDocument right, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileComparison(left, right, options, result));

        public Task<FileComparison> RecompareAsync(
            FileComparison comparison, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(comparison);

        public JsonDisplay FormatJsonForDisplay(
            FileComparison comparison, bool prettyLeft, bool prettyRight, Fubar.Diff.Core.Json.JsonFormatOptions format) =>
            new(comparison.OriginalLeftText, comparison.OriginalRightText, comparison.OriginalSemanticChanges);

        public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) => comparison;
    }

    private sealed class NoopMerge : IMergeService
    {
        public Task<string> SaveAsync(
            FileComparison comparison, MergeState state, DiffSide baseSide,
            string? targetPath = null, CancellationToken cancellationToken = default) => Task.FromResult("x");

        public string Preview(FileComparison comparison, MergeState state, DiffSide baseSide) => string.Empty;

        public Task<string> SaveThreeWayAsync(
            ThreeWayComparison comparison, ThreeWayMergeState state, MergeSide destination,
            string? targetPath = null, CancellationToken cancellationToken = default) => Task.FromResult("x");

        public Task<string> SaveThreeWayTextAsync(
            ThreeWayComparison comparison, MergeSide destination, IReadOnlyList<string> lines,
            string? targetPath = null, CancellationToken cancellationToken = default) => Task.FromResult("x");

        public string PreviewThreeWay(ThreeWayComparison c, ThreeWayMergeState s, MergeSide d) => string.Empty;
    }

    private sealed class Picker(string? save) : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickFilesAsync(string title) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult(save);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class Clipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text)
        {
            Text = text;

            return Task.CompletedTask;
        }
    }

    private sealed class Writer : ITextFileWriter
    {
        public string? Path { get; private set; }

        public IReadOnlyList<string>? Lines { get; private set; }

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default)
        {
            Path = path;
            Lines = lines;

            return Task.CompletedTask;
        }
    }

    private sealed class Watcher : IFileChangeWatcher
    {
        public event EventHandler? Changed;

        public void Watch(IReadOnlyList<string> paths) => _ = Changed;

        public void Stop() { }

        public void Dispose() { }
    }

    private static DiffResult Changed() => DiffResult.Create(
    [
        new DiffLine(1, "context", 1, "context", ChangeKind.Unchanged),
        new DiffLine(2, "old", 2, "new", ChangeKind.Modified),
    ]);

    private static DiffResult Identical() => DiffResult.Create(
    [
        new DiffLine(1, "same", 1, "same", ChangeKind.Unchanged),
    ]);

    private static (ComparisonViewModel Tab, Clipboard Clip, Writer Out) Build(DiffResult result, string? savePath = null)
    {
        var clip = new Clipboard();
        var writer = new Writer();

        var tab = new ComparisonViewModel(
            new StubComparison(result), new NoopMerge(), new Picker(savePath), new Watcher(),
            clip, writer, new ThemeManagerViewModel())
        {
            LeftPath = @"C:\one\before.cs",
            RightPath = @"C:\two\after.cs",
        };

        return (tab, clip, writer);
    }

    [AvaloniaFact]
    public async Task A_patch_is_offered_only_when_something_changed()
    {
        var (changed, _, _) = Build(Changed());
        await changed.CompareAsync();
        Assert.True(changed.HasPatch);

        var (identical, _, _) = Build(Identical());
        await identical.CompareAsync();
        Assert.False(identical.HasPatch);
    }

    [AvaloniaFact]
    public async Task Copying_puts_a_unified_diff_on_the_clipboard()
    {
        var (tab, clip, _) = Build(Changed());
        await tab.CompareAsync();

        await tab.CopyPatchCommand.ExecuteAsync(null);

        Assert.Contains("--- a/before.cs", clip.Text!, StringComparison.Ordinal);
        Assert.Contains("+++ b/after.cs", clip.Text!, StringComparison.Ordinal);
        Assert.Contains("-old", clip.Text!, StringComparison.Ordinal);
        Assert.Contains("+new", clip.Text!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task The_patch_names_the_files_not_their_full_paths()
    {
        // A patch is read and applied elsewhere; someone else's absolute paths are noise at best and
        // fail to apply at worst.
        var (tab, clip, _) = Build(Changed());
        await tab.CompareAsync();

        await tab.CopyPatchCommand.ExecuteAsync(null);

        Assert.DoesNotContain(@"C:\one", clip.Text!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Saving_writes_the_patch_to_the_chosen_file()
    {
        var (tab, _, writer) = Build(Changed(), @"C:\out\change.patch");
        await tab.CompareAsync();

        await tab.ExportPatchCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\out\change.patch", writer.Path);
        Assert.Contains("-old", writer.Lines!);
        Assert.Contains("+new", writer.Lines!);
    }

    [AvaloniaFact]
    public async Task A_cancelled_save_writes_nothing()
    {
        var (tab, _, writer) = Build(Changed(), savePath: null);
        await tab.CompareAsync();

        await tab.ExportPatchCommand.ExecuteAsync(null);

        Assert.Null(writer.Path);
    }

    [AvaloniaFact]
    public async Task Nothing_is_copied_when_there_is_nothing_to_copy()
    {
        var (tab, clip, _) = Build(Identical());
        await tab.CompareAsync();

        await tab.CopyPatchCommand.ExecuteAsync(null);

        Assert.Null(clip.Text);
    }
}
