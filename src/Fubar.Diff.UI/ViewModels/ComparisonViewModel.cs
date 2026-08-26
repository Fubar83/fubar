using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// One comparison: pick two files, compare them, page through the changes, resolve hunks, save.
///
/// This is the per-TAB view model - the window may hold several, each with its own files, options,
/// merge decisions and scroll position. Shared concerns (the theme, the settings file) are owned by
/// <see cref="ShellViewModel"/> and passed in.
///
/// It holds no diff or merge logic of its own - alignment belongs to <see cref="IFileComparisonService"/>,
/// the next/previous rules to <see cref="HunkNavigator"/>, and what gets written to
/// <see cref="MergedDocument"/>, all in Core. What lives here is the UI state those produce.
/// </summary>
public partial class ComparisonViewModel : ViewModelBase
{
    private readonly IFileComparisonService _comparisonService;
    private readonly IMergeService _mergeService;
    private readonly IFilePickerService _filePicker;

    private FileComparison _comparison = FileComparison.Empty;
    private MergeState _mergeState = MergeState.Empty;

    /// <summary>
    /// Suppresses persistence while defaults are being applied - otherwise each property assignment
    /// would fire its own change handler and write the settings file back.
    /// </summary>
    private bool _loadingSettings;

    public ComparisonViewModel(
        IFileComparisonService comparisonService,
        IMergeService mergeService,
        IFilePickerService filePicker,
        ThemeManagerViewModel themeManager)
    {
        _comparisonService = comparisonService;
        _mergeService = mergeService;
        _filePicker = filePicker;
        ThemeManager = themeManager;

        Pane.Navigated += OnPaneNavigated;
    }

    /// <summary>
    /// Raised when an option changes, so the shell can persist it as the default for new tabs. The tab
    /// deliberately does not write the settings file itself - several tabs doing so independently
    /// would race, and the last one to finish would win regardless of which the user touched.
    /// </summary>
    public event System.EventHandler? OptionsChanged;

    /// <summary>
    /// Raised after a comparison reads both files successfully, so the shell can add the pair to the
    /// recent list. The list is shared across tabs, so the shell owns it.
    /// </summary>
    public event System.EventHandler? ComparisonSucceeded;

    /// <summary>The label shown on this tab.</summary>
    public string Title => _comparison.HasBothSides
        ? $"{_comparison.Left.DisplayName} ↔ {_comparison.Right.DisplayName}"
        : "New comparison";

    /// <summary>
    /// Seeds this tab's options from the persisted defaults, without triggering a save or a
    /// re-comparison. Options are per-tab on purpose - a JSON comparison and a log comparison want
    /// different settings - and the persisted values are only the starting point for a new one.
    /// </summary>
    public void ApplyDefaults(AppSettings settings)
    {
        _loadingSettings = true;
        try
        {
            IgnoreWhitespace = settings.IgnoreWhitespace;
            IgnoreCase = settings.IgnoreCase;
            ReportPropertyOrder = settings.ReportPropertyOrder;
            MatchArraysByPosition = settings.MatchArraysByPosition;
            Mode = settings.Mode;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    /// <summary>The current option values, for the shell to persist as the defaults.</summary>
    public AppSettings CaptureOptions(AppSettings settings) => settings with
    {
        IgnoreWhitespace = IgnoreWhitespace,
        IgnoreCase = IgnoreCase,
        ReportPropertyOrder = ReportPropertyOrder,
        MatchArraysByPosition = MatchArraysByPosition,
        Mode = Mode,
    };

    public ThemeManagerViewModel ThemeManager { get; }

    /// <summary>
    /// Loads and compares a pair of files. Kept out of the constructor: file I/O there would block the
    /// UI thread before the first frame, and errors would have nowhere to be shown.
    /// </summary>
    public async Task InitializeAsync(string? left, string? right)
    {
        LeftPath = left ?? string.Empty;
        RightPath = right ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(LeftPath) && !string.IsNullOrWhiteSpace(RightPath))
        {
            await CompareAsync().ConfigureAwait(true);
        }
    }

    // ---- The diff pane ---------------------------------------------------------------------

    /// <summary>
    /// Everything about DISPLAYING the comparison - the two documents, hunks, navigation and the
    /// semantic tree. Held rather than inherited so the same widget can be hosted by API Studio,
    /// which produces its comparisons from memory rather than from files.
    /// </summary>
    public DiffPaneViewModel Pane { get; } = new();

    // ---- File selection ----------------------------------------------------------------------

    [ObservableProperty]
    public partial string LeftPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RightPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Choose two files to compare.";

    /// <summary>True while a comparison or save is running, so the view can disable the toolbar.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Set when the last attempt failed; the view shows it as an error banner.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // ---- Comparison options -------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IgnoreWhitespace { get; set; }

    [ObservableProperty]
    public partial bool IgnoreCase { get; set; }

    [ObservableProperty]
    public partial bool NormalizeStructure { get; set; }

    /// <summary>
    /// Report a property that only moved. Off by default because JSON objects are unordered, so
    /// reporting order produces noise on files nobody meaningfully edited.
    /// </summary>
    [ObservableProperty]
    public partial bool ReportPropertyOrder { get; set; }

    /// <summary>Compare arrays by position instead of by identity key.</summary>
    [ObservableProperty]
    public partial bool MatchArraysByPosition { get; set; }

    /// <summary>Text or semantic comparison; <see cref="ComparisonMode.Auto"/> decides per file.</summary>
    [ObservableProperty]
    public partial ComparisonMode Mode { get; set; } = ComparisonMode.Auto;

    /// <summary>The values offered by the mode selector.</summary>
    public static IReadOnlyList<ComparisonMode> ModeOptions { get; } = Enum.GetValues<ComparisonMode>();

    partial void OnIgnoreWhitespaceChanged(bool value) => OptionChanged();

    partial void OnIgnoreCaseChanged(bool value) => OptionChanged();

    partial void OnNormalizeStructureChanged(bool value) => OptionChanged();

    partial void OnReportPropertyOrderChanged(bool value) => OptionChanged();

    partial void OnMatchArraysByPositionChanged(bool value) => OptionChanged();

    partial void OnModeChanged(ComparisonMode value) => OptionChanged();

    /// <summary>
    /// An option was toggled: re-run the comparison and remember the choice. Both are skipped while
    /// settings are being applied at startup, which would otherwise re-save what was just loaded.
    /// </summary>
    private void OptionChanged()
    {
        if (_loadingSettings)
        {
            return;
        }

        Recompare();
        OptionsChanged?.Invoke(this, System.EventArgs.Empty);
    }

    // ---- Merge --------------------------------------------------------------------------------

    /// <summary>True once at least one hunk has been resolved, i.e. there is something to save.</summary>
    [ObservableProperty]
    public partial bool HasUnsavedMerge { get; set; }

    /// <summary>
    /// Which document the merge is written into. Right by convention: left is the original / theirs,
    /// right is the current / mine.
    /// </summary>
    [ObservableProperty]
    public partial DiffSide MergeTarget { get; set; } = DiffSide.Right;

    // ---- Commands -----------------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseLeftAsync()
    {
        if (await _filePicker.PickFileAsync("Choose the left-hand file").ConfigureAwait(true) is { } path)
        {
            LeftPath = path;
            await CompareAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task BrowseRightAsync()
    {
        if (await _filePicker.PickFileAsync("Choose the right-hand file").ConfigureAwait(true) is { } path)
        {
            RightPath = path;
            await CompareAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Runs the comparison. A no-op until both sides have been chosen.</summary>
    [RelayCommand]
    public async Task CompareAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftPath) || string.IsNullOrWhiteSpace(RightPath))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            _comparison = await _comparisonService
                .CompareFilesAsync(LeftPath, RightPath, CurrentOptions())
                .ConfigureAwait(true);

            // A fresh pair of files invalidates any decisions made about the previous one.
            _mergeState = MergeState.Empty;
            Refresh();

            // Announced only after a successful read - a path that could not be opened is not
            // something worth offering to reopen. The shell owns the recent list, since it is shared
            // across tabs.
            ComparisonSucceeded?.Invoke(this, System.EventArgs.Empty);
        }
        catch (TextFileReadException ex)
        {
            // The domain phrases these for a user already, so show the reason as-is.
            Reset();
            ErrorMessage = ex.Message;
            StatusMessage = "Comparison failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Resolves the current hunk in favour of the left side.</summary>
    [RelayCommand]
    private void TakeLeft() => Resolve(HunkResolution.TakeLeft);

    /// <summary>Resolves the current hunk in favour of the right side.</summary>
    [RelayCommand]
    private void TakeRight() => Resolve(HunkResolution.TakeRight);

    /// <summary>Undoes the decision on the current hunk.</summary>
    [RelayCommand]
    private void ResetHunk() => Resolve(HunkResolution.Unresolved);

    [RelayCommand]
    private Task SaveAsync() => SaveToAsync(targetPath: null);

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (await _filePicker.PickSaveFileAsync("Save merged file").ConfigureAwait(true) is { } path)
        {
            await SaveToAsync(path).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Loads a pair of files, e.g. from a drag and drop. Compares immediately when both are given;
    /// a single file fills whichever side is empty, so dropping two files one at a time works.
    /// </summary>
    public async Task OpenFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (paths.Count >= 2)
        {
            LeftPath = paths[0];
            RightPath = paths[1];
        }
        else if (string.IsNullOrWhiteSpace(LeftPath))
        {
            LeftPath = paths[0];
        }
        else
        {
            // Left is taken, so a single dropped file becomes the right-hand side - which is what
            // "compare this against what I already have open" means.
            RightPath = paths[0];
        }

        await CompareAsync().ConfigureAwait(true);
    }

    // ---- Internals ----------------------------------------------------------------------------

    private ComparisonOptions CurrentOptions() => new()
    {
        IgnoreWhitespace = IgnoreWhitespace,
        IgnoreCase = IgnoreCase,
        NormalizeStructure = NormalizeStructure,
        Mode = Mode,
        Json = new JsonComparisonOptions
        {
            ReportPropertyOrder = ReportPropertyOrder,
            MatchArraysByPosition = MatchArraysByPosition,
        },
    };

    private void Resolve(HunkResolution resolution)
    {
        if (Pane.CurrentHunk < 0 || Pane.CurrentHunk >= _comparison.Result.Hunks.Count)
        {
            return;
        }

        _mergeState = _mergeState.With(Pane.CurrentHunk, resolution);
        HasUnsavedMerge = _mergeState.HasResolutions;

        StatusMessage = resolution switch
        {
            HunkResolution.TakeLeft => $"Change {Pane.CurrentHunk + 1} resolved: left.",
            HunkResolution.TakeRight => $"Change {Pane.CurrentHunk + 1} resolved: right.",
            _ => $"Change {Pane.CurrentHunk + 1} reset.",
        };
    }

    private async Task SaveToAsync(string? targetPath)
    {
        if (!_comparison.HasBothSides)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var path = await _mergeService
                .SaveAsync(_comparison, _mergeState, MergeTarget, targetPath)
                .ConfigureAwait(true);

            HasUnsavedMerge = false;
            StatusMessage = $"Saved {path}";

            // Re-read from disk so the view reflects what was actually written, rather than a merge
            // preview that could drift from the file. Only for an in-place save: a Save As leaves the
            // compared pair untouched, so re-reading would show no change and look like it failed.
            if (targetPath is null)
            {
                await CompareAsync().ConfigureAwait(true);
            }
        }
        catch (TextFileWriteException ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Save failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        var result = _comparison.Result;

        // Decisions are keyed by hunk index, and re-running the diff can produce fewer hunks; drop any
        // that no longer exist so a stale index cannot silently resolve the wrong change.
        _mergeState = _mergeState.RemapTo(result.Hunks.Count);
        HasUnsavedMerge = _mergeState.HasResolutions;

        Pane.Show(
            result,
            _comparison.IsSemantic,
            _comparison.SemanticChanges,
            string.Join('\n', _comparison.Left.Lines),
            string.Join('\n', _comparison.Right.Lines));

        // A skipped semantic pass is only worth mentioning when the user explicitly asked for JSON;
        // the service decides that and leaves the reason null otherwise.
        ErrorMessage = _comparison.SemanticFallbackReason;

        RaiseTitle();

        StatusMessage = BuildStatus(result);
    }

    /// <summary>
    /// The status line. When a semantic comparison finds nothing, it says so explicitly rather than
    /// just "identical" - the two files may well differ as text, and claiming otherwise would look
    /// like a bug to anyone who can see that they do.
    /// </summary>
    private string BuildStatus(DiffResult result)
    {
        if (result.AreIdentical)
        {
            return Pane.IsSemantic
                ? "No semantic differences - the files differ only in formatting or ordering."
                : "The files are identical.";
        }

        if (!Pane.IsSemantic)
        {
            return $"{result.Hunks.Count} change(s) - {result.Inserted} added, "
                   + $"{result.Deleted} removed, {result.Modified} changed";
        }

        // Count SEMANTIC changes, not rows. The two genuinely differ - a value that changed and also
        // moved shows up as a deleted row and an inserted row, so the row count would say "2 changes"
        // where the tree shows one. Reporting the row count next to a tree that disagrees with it just
        // looks like a bug.
        // Ignored changes are excluded: they form no hunk and are drawn only as a faint band, so
        // counting them here would contradict both the tree and the region count beside it.
        var count = _comparison.SemanticChanges.Count(c => !c.IsIgnored);
        var ignored = _comparison.SemanticChanges.Count - count;
        var hunks = result.Hunks.Count;

        var suffix = ignored > 0 ? $"   ·   {ignored} ignored" : string.Empty;
        return $"semantic: {count} change(s) across {hunks} region(s){suffix}";
    }

    private void Reset()
    {
        _comparison = FileComparison.Empty;
        _mergeState = MergeState.Empty;
        HasUnsavedMerge = false;
        Pane.Clear();

        RaiseTitle();
    }

    /// <summary>
    /// The tab label is computed from the loaded documents, so it has to be raised by hand whenever
    /// the comparison is replaced - it has no setter for the generator to hook.
    /// </summary>
    private void RaiseTitle() => OnPropertyChanged(nameof(Title));

    /// <summary>
    /// Cancels the in-flight re-comparison, if any. Toggling several options quickly would otherwise
    /// queue a diff per keystroke and apply them out of order.
    /// </summary>
    private System.Threading.CancellationTokenSource? _recompareCancellation;

    /// <summary>
    /// Re-runs against the loaded documents when an option changes. Silent when nothing is loaded -
    /// toggling a checkbox before choosing files is not an error.
    /// </summary>
    private async void Recompare()
    {
        if (!_comparison.HasBothSides)
        {
            return;
        }

        var previous = _recompareCancellation;
        var cancellation = new System.Threading.CancellationTokenSource();
        _recompareCancellation = cancellation;

        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var result = await _comparisonService
                .RecompareAsync(_comparison, CurrentOptions(), cancellation.Token)
                .ConfigureAwait(true);

            // A newer toggle superseded this one while it ran; applying it now would show a result for
            // options the user has already moved on from.
            if (!cancellation.IsCancellationRequested)
            {
                _comparison = result;
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer toggle - nothing to report.
        }
    }

    /// <summary>Reports the position after the pane navigates. Navigation itself lives on the pane.</summary>
    private void OnPaneNavigated(object? sender, EventArgs e) =>
        StatusMessage = $"Change {Pane.CurrentHunk + 1} of {Pane.Hunks.Count}";
}
