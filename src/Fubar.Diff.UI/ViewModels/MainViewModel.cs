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
using Fubar.Diff.Core.Rendering;
using Fubar.Diff.UI.Services;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// The shell view model: pick two files, compare them, page through the changes, resolve hunks, save.
///
/// It holds no diff or merge logic of its own - alignment belongs to <see cref="IFileComparisonService"/>,
/// the next/previous rules to <see cref="HunkNavigator"/>, and what gets written to
/// <see cref="MergedDocument"/>, all in Core. What lives here is the UI state those produce.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IFileComparisonService _comparisonService;
    private readonly IMergeService _mergeService;
    private readonly IFilePickerService _filePicker;
    private readonly bool _compareOnLoad;

    private FileComparison _comparison = FileComparison.Empty;
    private MergeState _mergeState = MergeState.Empty;

    public MainViewModel(
        IFileComparisonService comparisonService,
        IMergeService mergeService,
        IFilePickerService filePicker,
        ThemeManagerViewModel themeManager,
        StartupFiles startupFiles)
    {
        _comparisonService = comparisonService;
        _mergeService = mergeService;
        _filePicker = filePicker;
        ThemeManager = themeManager;

        LeftPath = startupFiles.Left ?? string.Empty;
        RightPath = startupFiles.Right ?? string.Empty;
        _compareOnLoad = startupFiles.HasBoth;
    }

    public ThemeManagerViewModel ThemeManager { get; }

    /// <summary>
    /// Runs the startup comparison, if two files were named on the command line. Kept out of the
    /// constructor: file I/O there would block the UI thread before the first frame, and errors would
    /// have nowhere to be shown.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_compareOnLoad)
        {
            await CompareAsync().ConfigureAwait(true);
        }
    }

    // ---- Documents shown by the two editors --------------------------------------------------

    /// <summary>The left editor's flattened document, including filler lines.</summary>
    [ObservableProperty]
    public partial AlignedDocument? LeftDocument { get; set; }

    /// <summary>The right editor's flattened document, including filler lines.</summary>
    [ObservableProperty]
    public partial AlignedDocument? RightDocument { get; set; }

    /// <summary>Total display rows - the denominator the diff map scales positions against.</summary>
    public int TotalLines => _comparison.Result.Lines.Count;

    /// <summary>True when the semantic JSON pass ran, which is what enables the tree view.</summary>
    [ObservableProperty]
    public partial bool IsSemantic { get; set; }

    /// <summary>The semantic changes as a tree, for the Tree view.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<JsonChangeNodeViewModel> SemanticTree { get; set; } = [];

    /// <summary>Which view the diff pane shows. Only meaningful once a semantic comparison has run.</summary>
    [ObservableProperty]
    public partial DiffViewMode ViewMode { get; set; } = DiffViewMode.Text;

    /// <summary>The values offered by the view selector.</summary>
    public static IReadOnlyList<DiffViewMode> ViewModeOptions { get; } = Enum.GetValues<DiffViewMode>();

    /// <summary>Whether the side-by-side editors are the visible pane.</summary>
    public bool IsTextViewVisible => LeftDocument is not null && ViewMode == DiffViewMode.Text;

    /// <summary>
    /// Whether the change tree is the visible pane. Requires a semantic comparison - the tree would
    /// otherwise be permanently empty and look broken.
    /// </summary>
    public bool IsTreeViewVisible => LeftDocument is not null && ViewMode == DiffViewMode.Tree && IsSemantic;

    partial void OnViewModeChanged(DiffViewMode value) => RaiseViewVisibility();

    partial void OnIsSemanticChanged(bool value)
    {
        // Leaving the tree selected when the next comparison is plain text would show an empty pane,
        // so fall back to the view that always works.
        if (!value && ViewMode == DiffViewMode.Tree)
        {
            ViewMode = DiffViewMode.Text;
        }

        RaiseViewVisibility();
    }

    partial void OnLeftDocumentChanged(AlignedDocument? value) => RaiseViewVisibility();

    private void RaiseViewVisibility()
    {
        OnPropertyChanged(nameof(IsTextViewVisible));
        OnPropertyChanged(nameof(IsTreeViewVisible));
    }

    /// <summary>The hunks, for the diff map and navigation.</summary>
    public IReadOnlyList<DiffHunk> Hunks => _comparison.Result.Hunks;

    /// <summary>The rows, so the diff map can colour each tick by change kind.</summary>
    public IReadOnlyList<DiffLine> Lines => _comparison.Result.Lines;

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

    partial void OnIgnoreWhitespaceChanged(bool value) => Recompare();

    partial void OnIgnoreCaseChanged(bool value) => Recompare();

    partial void OnNormalizeStructureChanged(bool value) => Recompare();

    partial void OnReportPropertyOrderChanged(bool value) => Recompare();

    partial void OnMatchArraysByPositionChanged(bool value) => Recompare();

    partial void OnModeChanged(ComparisonMode value) => Recompare();

    // ---- Navigation ---------------------------------------------------------------------------

    /// <summary>Index into the current hunk list, or -1 when nothing is selected yet.</summary>
    [ObservableProperty]
    public partial int CurrentHunk { get; set; } = -1;

    /// <summary>Row to bring into view. The view watches this and scrolls; -1 means nothing pending.</summary>
    [ObservableProperty]
    public partial int ScrollToRow { get; set; } = -1;

    /// <summary>First visible row, pushed up by the view so the diff map can draw its viewport box.</summary>
    [ObservableProperty]
    public partial int ViewportStart { get; set; }

    /// <summary>Number of visible rows, pushed up by the view.</summary>
    [ObservableProperty]
    public partial int ViewportLength { get; set; }

    public bool HasChanges => _comparison.Result.Hunks.Count > 0;

    /// <summary>
    /// True when a specific change is selected. The merge commands act on the CURRENT hunk, so
    /// without this they would appear available before the user has picked one and then silently do
    /// nothing when clicked.
    /// </summary>
    public bool HasCurrentHunk => CurrentHunk >= 0 && CurrentHunk < _comparison.Result.Hunks.Count;

    partial void OnCurrentHunkChanged(int value) => OnPropertyChanged(nameof(HasCurrentHunk));

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

    [RelayCommand]
    private void NextChange() => MoveTo(HunkNavigator.Next(_comparison.Result.Hunks, CurrentHunk));

    [RelayCommand]
    private void PreviousChange() => MoveTo(HunkNavigator.Previous(_comparison.Result.Hunks, CurrentHunk));

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

    /// <summary>Jumps to a row, e.g. from a diff-map click, syncing the hunk selection to match.</summary>
    public void JumpToRow(int rowIndex)
    {
        ScrollToRow = rowIndex;
        CurrentHunk = HunkNavigator.IndexOfHunkContaining(_comparison.Result.Hunks, rowIndex);
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
        if (CurrentHunk < 0 || CurrentHunk >= _comparison.Result.Hunks.Count)
        {
            return;
        }

        _mergeState = _mergeState.With(CurrentHunk, resolution);
        HasUnsavedMerge = _mergeState.HasResolutions;

        StatusMessage = resolution switch
        {
            HunkResolution.TakeLeft => $"Change {CurrentHunk + 1} resolved: left.",
            HunkResolution.TakeRight => $"Change {CurrentHunk + 1} resolved: right.",
            _ => $"Change {CurrentHunk + 1} reset.",
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

        LeftDocument = AlignedText.Build(result, DiffSide.Left);
        RightDocument = AlignedText.Build(result, DiffSide.Right);

        CurrentHunk = -1;
        ScrollToRow = -1;

        IsSemantic = _comparison.IsSemantic;
        SemanticTree = JsonChangeNodeViewModel.Build(_comparison.SemanticChanges);

        // A skipped semantic pass is only worth mentioning when the user explicitly asked for JSON;
        // the service decides that and leaves the reason null otherwise.
        ErrorMessage = _comparison.SemanticFallbackReason;

        RaiseDerived();

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
            return IsSemantic
                ? "No semantic differences - the files differ only in formatting or ordering."
                : "The files are identical.";
        }

        if (!IsSemantic)
        {
            return $"{result.Hunks.Count} change(s) - {result.Inserted} added, "
                   + $"{result.Deleted} removed, {result.Modified} changed";
        }

        // Count SEMANTIC changes, not rows. The two genuinely differ - a value that changed and also
        // moved shows up as a deleted row and an inserted row, so the row count would say "2 changes"
        // where the tree shows one. Reporting the row count next to a tree that disagrees with it just
        // looks like a bug.
        var count = _comparison.SemanticChanges.Count;
        var hunks = result.Hunks.Count;

        return $"semantic: {count} change(s) across {hunks} region(s)";
    }

    private void Reset()
    {
        _comparison = FileComparison.Empty;
        _mergeState = MergeState.Empty;
        HasUnsavedMerge = false;
        LeftDocument = null;
        RightDocument = null;
        CurrentHunk = -1;
        ScrollToRow = -1;

        RaiseDerived();
    }

    /// <summary>
    /// Notifies the computed properties that read through to <c>_comparison</c>. They have no setter
    /// for the generator to hook, so they must be raised by hand whenever the comparison is replaced.
    /// </summary>
    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasCurrentHunk));
        OnPropertyChanged(nameof(TotalLines));
        OnPropertyChanged(nameof(Hunks));
        OnPropertyChanged(nameof(Lines));
    }

    /// <summary>
    /// Re-runs against the loaded documents when an option changes. Silent when nothing is loaded -
    /// toggling a checkbox before choosing files is not an error.
    /// </summary>
    private void Recompare()
    {
        if (!_comparison.HasBothSides)
        {
            return;
        }

        _comparison = _comparisonService.Recompare(_comparison, CurrentOptions());
        Refresh();
    }

    private void MoveTo(int? hunkIndex)
    {
        if (hunkIndex is not { } index)
        {
            return;
        }

        CurrentHunk = index;
        ScrollToRow = _comparison.Result.Hunks[index].StartIndex;
        StatusMessage = $"Change {index + 1} of {_comparison.Result.Hunks.Count}";
    }
}
