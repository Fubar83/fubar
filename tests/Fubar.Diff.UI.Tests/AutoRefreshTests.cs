using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
/// What happens when the files change underneath a comparison.
///
/// The rule worth protecting is the refusal: a new comparison renumbers the hunks that merge decisions
/// are keyed by, so reloading over unsaved decisions would either discard them or apply them to
/// different changes. That is a choice for the user, not for a file-system event, and it is the sort of
/// data loss nobody notices until they save.
/// </summary>
public class AutoRefreshTests
{
    private sealed class FakeWatcher : IFileChangeWatcher
    {
        public event EventHandler? Changed;

        public IReadOnlyList<string>? Watching { get; private set; }

        public bool Stopped { get; private set; }

        public bool Disposed { get; private set; }

        public void Watch(IReadOnlyList<string> paths)
        {
            Watching = paths;
            Stopped = false;
        }

        public void Stop()
        {
            Watching = null;
            Stopped = true;
        }

        public void Dispose() => Disposed = true;

        /// <summary>Raises the event the way the real one does - from somewhere other than the UI thread.</summary>
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class StubComparisonService : IFileComparisonService
    {
        public int Comparisons { get; private set; }

        public Task<FileComparison> CompareFilesAsync(
            string leftPath, string rightPath, ComparisonOptions options, CancellationToken cancellationToken = default)
        {
            Comparisons++;

            var rows = new List<DiffLine> { new(1, "a", 1, "b", ChangeKind.Modified) };

            return Task.FromResult(new FileComparison(
                new TextDocument(leftPath, ["a"], TextFormat.Default),
                new TextDocument(rightPath, ["b"], TextFormat.Default),
                options,
                DiffResult.Create(rows)));
        }

        public Task<FileComparison> CompareTextAsync(
            string leftText, string rightText, ComparisonOptions options,
            string leftLabel = "left", string rightLabel = "right", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileComparison> RecompareAsync(
            FileComparison comparison, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(comparison);

        public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) => comparison;
    }

    /// <summary>Fails every read, for the "the file vanished mid-save" path.</summary>
    private sealed class VanishingComparisonService : IFileComparisonService
    {
        public int Attempts { get; private set; }

        public Task<FileComparison> CompareFilesAsync(
            string leftPath, string rightPath, ComparisonOptions options, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new TextFileReadException(leftPath, "the file does not exist.");
        }

        public Task<FileComparison> CompareTextAsync(
            string leftText, string rightText, ComparisonOptions options,
            string leftLabel = "left", string rightLabel = "right", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileComparison> RecompareAsync(
            FileComparison comparison, ComparisonOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(comparison);

        public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) => comparison;
    }

    private sealed class NoopMergeService : IMergeService
    {
        public Task<string> SaveAsync(
            FileComparison comparison, MergeState state, DiffSide baseSide,
            string? targetPath = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(targetPath ?? "right.txt");

        public string Preview(FileComparison comparison, MergeState state, DiffSide baseSide) => string.Empty;

        public Task<string> SaveThreeWayAsync(
            ThreeWayComparison comparison, ThreeWayMergeState state, MergeSide destination,
            string? targetPath = null, CancellationToken cancellationToken = default) =>
            Task.FromResult("merged");

        public string PreviewThreeWay(ThreeWayComparison comparison, ThreeWayMergeState state, MergeSide destination) =>
            string.Empty;
    }

    /// <summary>Records what would have been copied, so a patch export can be asserted.</summary>
    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text)
        {
            Text = text;

            return Task.CompletedTask;
        }
    }

    /// <summary>Records what would have been written.</summary>
    private sealed class FakeWriter : ITextFileWriter
    {
        public string? Path { get; private set; }

        public IReadOnlyList<string>? Lines { get; private set; }

        public Task WriteAsync(
            string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken cancellationToken = default)
        {
            Path = path;
            Lines = lines;

            return Task.CompletedTask;
        }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static (ComparisonViewModel Tab, FakeWatcher Watcher, StubComparisonService Service) Build()
    {
        var watcher = new FakeWatcher();
        var service = new StubComparisonService();

        var tab = new ComparisonViewModel(
            service, new NoopMergeService(), new NoPicker(), watcher,
            new FakeClipboard(), new FakeWriter(), new ThemeManagerViewModel())
        {
            LeftPath = "left.txt",
            RightPath = "right.txt",
        };

        return (tab, watcher, service);
    }

    /// <summary>Raises the watcher and pumps the dispatcher, since the handler marshals onto it.</summary>
    private static void SignalChange(FakeWatcher watcher)
    {
        watcher.RaiseChanged();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Comparing_starts_watching_both_files()
    {
        var (tab, watcher, _) = Build();

        await tab.CompareAsync();

        Assert.Equal(["left.txt", "right.txt"], watcher.Watching);
    }

    [AvaloniaFact]
    public async Task A_change_on_disk_re_runs_the_comparison()
    {
        var (tab, watcher, service) = Build();
        await tab.CompareAsync();

        SignalChange(watcher);

        Assert.Equal(2, service.Comparisons);
        Assert.False(tab.FilesChangedOnDisk);
    }

    [AvaloniaFact]
    public async Task Unsaved_merge_decisions_are_never_discarded_by_a_file_event()
    {
        // The rule this whole feature has to get right.
        var (tab, watcher, service) = Build();
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        tab.TakeLeftCommand.Execute(null);
        Assert.True(tab.HasUnsavedMerge);

        SignalChange(watcher);

        Assert.Equal(1, service.Comparisons);
        Assert.True(tab.FilesChangedOnDisk);
        Assert.True(tab.HasUnsavedMerge);
    }

    [AvaloniaFact]
    public async Task Reloading_explicitly_is_what_discards_them()
    {
        var (tab, watcher, service) = Build();
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        tab.TakeLeftCommand.Execute(null);
        SignalChange(watcher);

        await tab.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(2, service.Comparisons);
        Assert.False(tab.FilesChangedOnDisk);
        Assert.False(tab.HasUnsavedMerge);
    }

    [AvaloniaFact]
    public async Task Our_own_save_is_not_treated_as_an_external_change()
    {
        // Saving already re-reads the file deliberately. Acting on the watcher too would be a wasted
        // comparison, and would raise a "changed on disk" banner about the user's own save.
        var (tab, watcher, service) = Build();
        await tab.CompareAsync();

        tab.Pane.CurrentHunk = 0;
        tab.TakeLeftCommand.Execute(null);

        await tab.SaveCommand.ExecuteAsync(null);
        var afterSave = service.Comparisons;

        SignalChange(watcher);

        Assert.Equal(afterSave, service.Comparisons);
        Assert.False(tab.FilesChangedOnDisk);
    }

    [AvaloniaFact]
    public async Task Turning_it_off_stops_watching()
    {
        var (tab, watcher, service) = Build();
        await tab.CompareAsync();

        tab.AutoRefresh = false;

        Assert.True(watcher.Stopped);

        SignalChange(watcher);
        Assert.Equal(1, service.Comparisons);
    }

    [AvaloniaFact]
    public async Task Turning_it_back_on_starts_watching_again()
    {
        var (tab, watcher, _) = Build();
        await tab.CompareAsync();

        tab.AutoRefresh = false;
        tab.AutoRefresh = true;

        Assert.Equal(["left.txt", "right.txt"], watcher.Watching);
    }

    [AvaloniaFact]
    public void Nothing_is_watched_before_a_comparison_has_run()
    {
        var (_, watcher, _) = Build();

        Assert.Null(watcher.Watching);
    }

    [AvaloniaFact]
    public async Task A_file_that_vanishes_mid_save_leaves_the_previous_result_alone()
    {
        // Editors that save by replacing a file leave it missing for an instant. Turning that into an
        // error banner over a diff that was fine would be worse than waiting; the banner offers a
        // manual reload instead.
        var watcher = new FakeWatcher();
        var service = new VanishingComparisonService();

        var tab = new ComparisonViewModel(
            service, new NoopMergeService(), new NoPicker(), watcher,
            new FakeClipboard(), new FakeWriter(), new ThemeManagerViewModel())
        {
            LeftPath = "gone.txt",
            RightPath = "also-gone.txt",
        };

        await tab.CompareAsync();
        Assert.NotNull(tab.ErrorMessage);

        // The first compare failed, so nothing is being watched and no event can arrive - which is
        // itself the right behaviour, and all this asserts is that it did not throw on the way there.
        Assert.Null(watcher.Watching);
        Assert.Equal(1, service.Attempts);
    }

    [AvaloniaFact]
    public void Closing_the_tab_disposes_the_watcher()
    {
        // It owns OS handles; one leak per closed comparison would accumulate for the life of the window.
        var (tab, watcher, _) = Build();

        tab.Dispose();

        Assert.True(watcher.Disposed);
    }

    [AvaloniaFact]
    public void The_setting_is_persisted_and_restored()
    {
        var (tab, _, _) = Build();

        tab.ApplyDefaults(AppSettings.Default with { AutoRefresh = false });
        Assert.False(tab.AutoRefresh);

        Assert.False(tab.CaptureOptions(AppSettings.Default).AutoRefresh);
    }
}
