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
/// Refreshing a comparison: what F5 does, and how the window says the diff is no longer describing
/// what is on screen.
///
/// The two halves are one feature. Editing a pane makes the comparison stale for as long as it takes
/// to re-run - a fraction of a second with "re-compare as you type" on, and until the user asks with
/// it off - and a diff tool showing stale counts without saying so is the failure both halves exist to
/// prevent. Everything here drives the view models directly; typing is simulated the way the view
/// does it, through <c>Pane.FileLinesReader</c> + <c>Pane.ReportEdit</c>.
/// </summary>
public class RefreshTests
{
    /// <summary>Serves in-memory files, and lets a test change one underneath the comparison.</summary>
    private sealed class Files : ITextFileReader, ITextFileWriter
    {
        private readonly Dictionary<string, string[]> _files;

        public Files(params (string Path, string[] Lines)[] files) =>
            _files = files.ToDictionary(f => f.Path, f => f.Lines, StringComparer.Ordinal);

        public void Replace(string path, string[] lines) => _files[path] = lines;

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

            // Off, so the settle timer never races the assertions. Every test that cares about the
            // live path turns it back on deliberately.
            LiveDiff = false,
        };

        return (tab, disk);
    }

    /// <summary>Stands in for the editor: whatever a test says the edited pane now holds.</summary>
    private static void Type(ComparisonViewModel tab, DiffSide side, string[] lines)
    {
        tab.Pane.FileLinesReader = _ => lines;
        tab.Pane.ReportEdit(side);
    }

    [AvaloniaFact]
    public async Task A_fresh_comparison_is_not_stale()
    {
        var (tab, _) = Build(["a", "b"], ["a", "B"]);
        await tab.CompareAsync();

        Assert.False(tab.IsDiffStale);
    }

    [AvaloniaFact]
    public async Task Typing_marks_the_diff_stale()
    {
        var (tab, _) = Build(["a", "b"], ["a", "B"]);
        await tab.CompareAsync();

        Type(tab, DiffSide.Right, ["a", "b"]);

        // Said before the comparison catches up, which is the whole point: the counts on screen still
        // describe the text as it was before the keystroke.
        Assert.True(tab.IsDiffStale);
    }

    [AvaloniaFact]
    public async Task F5_compares_what_the_panes_now_hold()
    {
        var (tab, _) = Build(["a", "b"], ["a", "B"]);
        await tab.CompareAsync();
        Assert.True(tab.Pane.HasChanges);

        // Typed the right-hand side into agreement with the left.
        Type(tab, DiffSide.Right, ["a", "b"]);
        await tab.RefreshDiffCommand.ExecuteAsync(null);

        Assert.False(tab.Pane.HasChanges);
        Assert.False(tab.IsDiffStale);
    }

    [AvaloniaFact]
    public async Task F5_never_reloads_over_unsaved_edits()
    {
        // The rule that decides which of F5's two meanings applies. Going back to disk here would
        // discard text that exists nowhere else, which is not something a refresh key may do.
        var (tab, disk) = Build(["a", "b"], ["a", "B"]);
        await tab.CompareAsync();

        Type(tab, DiffSide.Right, ["a", "typed"]);
        disk.Replace("right.txt", ["a", "from disk"]);

        await tab.RefreshDiffCommand.ExecuteAsync(null);

        Assert.Contains("typed", tab.Pane.RightDocument!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("from disk", tab.Pane.RightDocument!.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task F5_with_nothing_typed_re_reads_the_files()
    {
        // The other meaning, and the one someone pressing F5 after a build or a checkout wants.
        var (tab, disk) = Build(["a", "b"], ["a", "b"]);
        await tab.CompareAsync();
        Assert.False(tab.Pane.HasChanges);

        disk.Replace("right.txt", ["a", "changed elsewhere"]);
        await tab.RefreshDiffCommand.ExecuteAsync(null);

        Assert.True(tab.Pane.HasChanges);
        Assert.Contains("changed elsewhere", tab.Pane.RightDocument!.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task F5_clears_the_files_changed_notice()
    {
        var (tab, _) = Build(["a"], ["a"]);
        await tab.CompareAsync();

        tab.FilesChangedOnDisk = true;
        await tab.RefreshDiffCommand.ExecuteAsync(null);

        Assert.False(tab.FilesChangedOnDisk);
    }

    [AvaloniaFact]
    public async Task With_live_diff_off_the_comparison_waits_for_F5()
    {
        var (tab, _) = Build(["a", "b"], ["a", "B"]);
        await tab.CompareAsync();

        Type(tab, DiffSide.Right, ["a", "b"]);

        // No timer was started, so nothing will happen on its own - the status bar's "Diff out of
        // date" is the only thing standing between the user and a stale answer.
        Assert.True(tab.IsDiffStale);
        Assert.True(tab.Pane.HasChanges);

        await tab.RefreshDiffCommand.ExecuteAsync(null);

        Assert.False(tab.IsDiffStale);
        Assert.False(tab.Pane.HasChanges);
    }

    [AvaloniaFact]
    public async Task Turning_live_diff_back_on_catches_up_at_once()
    {
        // Otherwise the pending edit would sit stale until the next keystroke started a timer.
        var (tab, _) = Build(["a", "b"], ["a", "B"]);
        await tab.CompareAsync();

        Type(tab, DiffSide.Right, ["a", "b"]);
        tab.LiveDiff = true;

        await SettleAsync(tab);

        Assert.False(tab.IsDiffStale);
        Assert.False(tab.Pane.HasChanges);
    }

    /// <summary>
    /// Waits for a re-diff that nothing can be awaited on.
    ///
    /// A property setter cannot await, so turning "re-compare as you type" back on starts the
    /// comparison and returns - which is right for the app and awkward for a test. Pumping the
    /// dispatcher is what lets the continuation (posted there by ConfigureAwait(true)) actually run.
    /// </summary>
    private static async Task SettleAsync(ComparisonViewModel tab)
    {
        for (var attempt = 0; attempt < 100 && tab.IsDiffStale; attempt++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
