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
/// The word-wrap setting's trip from the toolbar to the pane and back to the settings file.
///
/// A display setting, so the thing to check is that it never re-runs a comparison: wrapping changes
/// where the text breaks on screen and nothing at all about what differs.
/// </summary>
public class WordWrapSettingTests
{
    private sealed class CountingComparison : IFileComparisonService
    {
        public int Comparisons { get; private set; }

        public Task<FileComparison> CompareFilesAsync(
            string leftPath, string rightPath, ComparisonOptions options, CancellationToken cancellationToken = default)
        {
            Comparisons++;

            return Task.FromResult(new FileComparison(
                new TextDocument(leftPath, ["a"], TextFormat.Default),
                new TextDocument(rightPath, ["b"], TextFormat.Default),
                options,
                DiffResult.Create([new DiffLine(1, "a", 1, "b", ChangeKind.Modified)])));
        }

        public Task<FileComparison> CompareTextAsync(
            string leftText, string rightText, ComparisonOptions options,
            string leftLabel = "left", string rightLabel = "right", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileComparison> RecompareAsync(
            FileComparison comparison, ComparisonOptions options, CancellationToken cancellationToken = default)
        {
            Comparisons++;

            return Task.FromResult(comparison);
        }

        public FileComparison Recompare(FileComparison comparison, ComparisonOptions options)
        {
            Comparisons++;

            return comparison;
        }
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

        public string PreviewThreeWay(ThreeWayComparison c, ThreeWayMergeState s, MergeSide d) => string.Empty;
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

    private static (ComparisonViewModel Tab, CountingComparison Service) Build()
    {
        var service = new CountingComparison();

        var tab = new ComparisonViewModel(
            service, new NoopMerge(), new NoPicker(), new NoWatcher(),
            new NoClipboard(), new NoWriter(), new ThemeManagerViewModel())
        {
            LeftPath = "left.txt",
            RightPath = "right.txt",
        };

        return (tab, service);
    }

    [AvaloniaFact]
    public void The_toolbar_toggle_reaches_the_pane()
    {
        var (tab, _) = Build();

        tab.WordWrap = true;

        Assert.True(tab.Pane.WordWrap);
    }

    [AvaloniaFact]
    public async Task Toggling_it_never_re_runs_the_comparison()
    {
        // A display setting. Re-comparing would also renumber the hunks, which is how merge decisions
        // get silently reattached to different changes.
        var (tab, service) = Build();
        await tab.CompareAsync();

        var before = service.Comparisons;

        tab.WordWrap = true;
        tab.WordWrap = false;

        Assert.Equal(before, service.Comparisons);
    }

    [AvaloniaFact]
    public async Task A_new_comparison_keeps_the_setting()
    {
        var (tab, _) = Build();
        tab.WordWrap = true;

        await tab.CompareAsync();

        Assert.True(tab.Pane.WordWrap);
    }

    [AvaloniaFact]
    public void It_is_off_by_default()
    {
        // A wrapped line has no fixed height, so a screen holds fewer changes and the reader loses the
        // ability to scan down a column. Worth having, not worth imposing.
        var (tab, _) = Build();

        Assert.False(tab.WordWrap);
        Assert.False(AppSettings.Default.WordWrap);
    }

    [AvaloniaFact]
    public void It_is_persisted_and_restored()
    {
        var (tab, _) = Build();

        tab.ApplyDefaults(AppSettings.Default with { WordWrap = true });

        Assert.True(tab.WordWrap);
        Assert.True(tab.Pane.WordWrap);
        Assert.True(tab.CaptureOptions(AppSettings.Default).WordWrap);
    }
}
