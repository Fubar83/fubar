using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// The toolbar's View menu, and what the status bar says while stepping through a JSON comparison.
///
/// Both come from the same trim: five toolbar controls (a Compare label and combo, a side-by-side /
/// unified switch, two toggles) became one menu, and the Json view's own Prev/Next strip - which
/// carried the "2 of 5" caption - went away in favour of the toolbar's buttons and the status bar.
/// Nothing that strip said may be lost with it.
/// </summary>
public class ViewMenuTests
{
    private sealed class Files(Dictionary<string, string> files) : ITextFileReader, ITextFileWriter
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextDocument(path, files[path].Split('\n'), TextFormat.Default));

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default) =>
            Task.CompletedTask;
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

    private static ComparisonViewModel Build(string left, string right)
    {
        var disk = new Files(new Dictionary<string, string> { ["l.json"] = left, ["r.json"] = right });

        var comparisons = new FileComparisonService(
            disk,
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        return new ComparisonViewModel(
            comparisons, new MergeService(disk), new NoPicker(), new NoWatcher(),
            new NoClipboard(), disk, new ThemeManagerViewModel())
        {
            LeftPath = "l.json",
            RightPath = "r.json",
        };
    }

    private const string Left = """{"a": 1, "b": 1}""";

    private const string Right = """{"a": 2, "b": 2}""";

    [AvaloniaFact]
    public void The_menu_ticks_the_mode_that_is_in_force()
    {
        var tab = Build(Left, Right);

        Assert.True(tab.IsModeAuto);

        tab.SetCompareModeCommand.Execute(ComparisonMode.Text);

        Assert.Equal(ComparisonMode.Text, tab.Mode);
        Assert.True(tab.IsModeText);
        Assert.False(tab.IsModeAuto);
    }

    [AvaloniaFact]
    public void The_ticks_follow_the_mode_however_it_was_set()
    {
        // Set directly, as loading saved settings does. A menu that only updated when clicked would
        // open showing the wrong answer on the next run.
        var tab = Build(Left, Right);
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.Mode = ComparisonMode.Json;

        Assert.True(tab.IsModeJson);
        Assert.Contains(nameof(ComparisonViewModel.IsModeJson), raised);
    }

    [AvaloniaFact]
    public void The_layout_entries_switch_the_text_layout()
    {
        var tab = Build(Left, Right);

        tab.SetUnifiedCommand.Execute(null);
        Assert.Equal(DiffViewMode.Unified, tab.Pane.ViewMode);

        tab.SetSideBySideCommand.Execute(null);
        Assert.Equal(DiffViewMode.SideBySide, tab.Pane.ViewMode);
    }

    [AvaloniaFact]
    public async Task Stepping_through_a_JSON_comparison_reports_where_it_is()
    {
        // What the Json view's own strip used to say. Losing it would leave "5 changes across 3
        // regions" on screen however far through them you were.
        var tab = Build(Left, Right);
        await tab.CompareAsync();

        Assert.True(tab.Pane.IsJsonViewVisible);

        tab.Pane.NextDifferenceCommand.Execute(null);

        Assert.Contains("$.a", tab.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("1 of 2", tab.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_fresh_comparison_still_reports_its_summary()
    {
        // The caption's other form ("2 difference(s) - none selected") is raised whenever a comparison
        // loads, and must not overwrite the summary written there a moment earlier.
        var tab = Build(Left, Right);
        await tab.CompareAsync();

        Assert.Contains("semantic", tab.StatusMessage, StringComparison.Ordinal);
    }
}
