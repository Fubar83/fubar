using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// One three-way merge: pick the ancestor and the two edits, merge them, step through what could not
/// be settled automatically, resolve it, save.
///
/// The three-way sibling of <see cref="ComparisonViewModel"/>, and deliberately a separate view model
/// rather than a mode on it. A merge asks a different question, has a third document, resolves per
/// REGION rather than per hunk, and writes somewhere the user chooses; folding it in would have meant a
/// nullable third document and a mode flag threaded through every member of a class that is already
/// long.
///
/// Like its sibling it holds no merge logic of its own - the algorithm is <see cref="ThreeWayMerger"/>,
/// navigation is <see cref="MergeRegionNavigator"/>, and what gets written is
/// <see cref="ThreeWayMergedDocument"/>, all in Core.
/// </summary>
public partial class MergeViewModel : ViewModelBase
{
    private readonly IThreeWayComparisonService _comparisonService;
    private readonly IMergeService _mergeService;
    private readonly IFilePickerService _filePicker;

    private ThreeWayComparison _comparison = ThreeWayComparison.Empty;
    private ThreeWayMergeState _state = ThreeWayMergeState.Empty;

    /// <summary>
    /// The options the merge runs under.
    ///
    /// Seeded from the persisted defaults and not editable here, on purpose: every one of them is
    /// already reachable from the main window's Settings, and a second copy of that window would be
    /// two places to change the same preference with no way to tell which one is in force. What IS
    /// here is the one control that only means something during a merge - whether navigation stops on
    /// anything other than a conflict.
    /// </summary>
    private ComparisonOptions _options = ComparisonOptions.Default;

    public MergeViewModel(
        IThreeWayComparisonService comparisonService,
        IMergeService mergeService,
        IFilePickerService filePicker,
        ThemeManagerViewModel themeManager)
    {
        _comparisonService = comparisonService;
        _mergeService = mergeService;
        _filePicker = filePicker;
        ThemeManager = themeManager;

        Pane.Navigated += (_, _) => StatusMessage = Pane.RegionCaption;
    }

    public ThemeManagerViewModel ThemeManager { get; }

    /// <summary>Everything about DISPLAYING the merge - the three documents, regions and navigation.</summary>
    public ThreeWayPaneViewModel Pane { get; } = new();

    /// <summary>Seeds the options and display preferences from the persisted defaults.</summary>
    public void ApplyDefaults(AppSettings settings)
    {
        _options = new ComparisonOptions
        {
            IgnoreWhitespace = settings.IgnoreWhitespace,
            IgnoreCase = settings.IgnoreCase,
            NormalizeStructure = settings.NormalizeStructure,
            NormalizeUnicode = settings.NormalizeUnicode,
            Mode = settings.Mode,
            Code = new CodeComparisonOptions
            {
                IgnoreComments = settings.IgnoreComments,
                IgnoreBlankLines = settings.IgnoreBlankLines,
            },
            Json = new JsonComparisonOptions
            {
                ReportPropertyOrder = settings.ReportPropertyOrder,
                MatchArraysByPosition = settings.MatchArraysByPosition,
                IgnoreNullVsMissing = settings.IgnoreNullVsMissing,
                ArrayKeyOverrides = settings.ArrayKeyOverrides,
                IgnoredPaths = settings.IgnoredPaths,
            },
        };

        Pane.ShowInvisibles = settings.ShowInvisibles;
        Pane.SyntaxHighlighting = settings.SyntaxHighlighting;
    }

    // ---- File selection -------------------------------------------------------------------------

    [ObservableProperty]
    public partial string BasePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LeftPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RightPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Choose a common ancestor and the two files to merge.";

    /// <summary>Set when the last attempt failed; the view shows it as an error banner.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>True while a merge or save is running, so the view can disable the toolbar.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Whether the pickers are shown in full, or collapsed to a summary line - the same bargain the
    /// two-way window makes, and it matters more here, because three pickers is a whole band of chrome
    /// standing between the user and three columns of text.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFileRowExpanded { get; set; } = true;

    [RelayCommand]
    private void ToggleFileRow() => IsFileRowExpanded = !IsFileRowExpanded;

    /// <summary>The one-line summary shown once the pickers collapse.</summary>
    public string FileSummary => _comparison.HasAllSides
        ? $"{_comparison.Left.DisplayName}  ↔  {_comparison.Ancestor.DisplayName}  ↔  {_comparison.Right.DisplayName}"
        : "No files chosen";

    // ---- Merge state ----------------------------------------------------------------------------

    /// <summary>
    /// Which file's path and format the merged result takes. Right by default - "mine" is the file in
    /// front of you and the one a merge is normally being resolved INTO.
    /// </summary>
    [ObservableProperty]
    public partial MergeSide MergeDestination { get; set; } = MergeSide.Right;

    /// <summary>The destinations offered by the selector.</summary>
    public static IReadOnlyList<MergeSide> DestinationOptions { get; } = Enum.GetValues<MergeSide>();

    /// <summary>How many conflicts still have no decision.</summary>
    public int UnresolvedConflicts => _state.UnresolvedConflicts(_comparison.Result);

    /// <summary>
    /// Whether to warn that saving now would keep the ancestor's version somewhere.
    ///
    /// Shown as a banner rather than blocking the save: stopping half way through a long merge to save
    /// what you have is a legitimate thing to want, and the domain has a defined answer for an
    /// unresolved region. What is NOT acceptable is that answer being a surprise.
    /// </summary>
    public bool HasUnresolvedConflicts => UnresolvedConflicts > 0;

    /// <summary>Names what would happen on save, for the banner.</summary>
    public string UnresolvedDetail =>
        $"{UnresolvedConflicts} conflict(s) still unresolved - saving now keeps the base version for those.";

    /// <summary>True once the merge has produced something worth saving.</summary>
    public bool CanSave => _comparison.HasAllSides;

    // ---- Commands -------------------------------------------------------------------------------

    [RelayCommand]
    private Task BrowseBaseAsync() => PickInto(path => BasePath = path, "Choose the common ancestor");

    [RelayCommand]
    private Task BrowseLeftAsync() => PickInto(path => LeftPath = path, "Choose the left-hand file (theirs)");

    [RelayCommand]
    private Task BrowseRightAsync() => PickInto(path => RightPath = path, "Choose the right-hand file (mine)");

    private async Task PickInto(Action<string> assign, string title)
    {
        if (await _filePicker.PickFileAsync(title).ConfigureAwait(true) is { } path)
        {
            assign(path);
            await MergeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Runs the merge. A no-op until all three files have been chosen.</summary>
    [RelayCommand]
    public async Task MergeAsync()
    {
        if (string.IsNullOrWhiteSpace(BasePath)
            || string.IsNullOrWhiteSpace(LeftPath)
            || string.IsNullOrWhiteSpace(RightPath))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            _comparison = await _comparisonService
                .CompareFilesAsync(BasePath, LeftPath, RightPath, _options)
                .ConfigureAwait(true);

            // A fresh set of files invalidates every decision made about the previous one.
            _state = ThreeWayMergeState.Empty;

            Pane.SyntaxExtension = System.IO.Path.GetExtension(_comparison.Right.Path);
            Pane.Show(_comparison.Result);

            IsFileRowExpanded = false;

            // Land on the first thing that needs a person, rather than making them press Next to find
            // out whether anything does.
            Pane.NextRegion();

            Refresh();
        }
        catch (TextFileReadException ex)
        {
            Reset();
            ErrorMessage = ex.Message;
            StatusMessage = "Merge failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void TakeBase() => Resolve(MergeChoice.TakeBase);

    [RelayCommand]
    private void TakeLeft() => Resolve(MergeChoice.TakeLeft);

    [RelayCommand]
    private void TakeRight() => Resolve(MergeChoice.TakeRight);

    /// <summary>Undoes the decision on the current region, putting it back to whatever the merge implies.</summary>
    [RelayCommand]
    private void ResetRegion() => Resolve(MergeChoice.Unresolved);

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
    /// Loads three dropped files. Order is ancestor, left, right - which is a guess, and why the
    /// pickers re-expand rather than the merge running blind on it.
    /// </summary>
    public async Task OpenFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count < 3)
        {
            return;
        }

        BasePath = paths[0];
        LeftPath = paths[1];
        RightPath = paths[2];

        await MergeAsync().ConfigureAwait(true);
    }

    // ---- Internals ------------------------------------------------------------------------------

    private void Resolve(MergeChoice choice)
    {
        if (Pane.CurrentRegion < 0 || Pane.CurrentRegion >= _comparison.Result.Regions.Count)
        {
            return;
        }

        _state = _state.With(Pane.CurrentRegion, choice);

        StatusMessage = choice switch
        {
            MergeChoice.TakeBase => $"Region {Pane.CurrentRegion + 1} resolved: base.",
            MergeChoice.TakeLeft => $"Region {Pane.CurrentRegion + 1} resolved: left.",
            MergeChoice.TakeRight => $"Region {Pane.CurrentRegion + 1} resolved: right.",
            _ => $"Region {Pane.CurrentRegion + 1} reset.",
        };

        RaiseMergeState();

        // Straight on to the next thing needing attention. Resolving a conflict is never the end of
        // the task, and making the user press Next after every single one is the difference between a
        // merge tool that is used and one that is tolerated.
        if (choice != MergeChoice.Unresolved)
        {
            Pane.NextRegion();
        }
    }

    private async Task SaveToAsync(string? targetPath)
    {
        if (!_comparison.HasAllSides)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var unresolved = UnresolvedConflicts;

            var path = await _mergeService
                .SaveThreeWayAsync(_comparison, _state, MergeDestination, targetPath)
                .ConfigureAwait(true);

            // Says out loud what was written for the regions nobody decided. Saving past a conflict is
            // allowed; being unaware of having done it is not.
            StatusMessage = unresolved > 0
                ? $"Saved {path} - {unresolved} unresolved conflict(s) kept the base version."
                : $"Saved {path}";
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
        // Decisions are keyed by region index, and re-running the merge can produce fewer regions; drop
        // any that no longer exist so a stale index cannot silently resolve the wrong one.
        _state = _state.RemapTo(_comparison.Result.Regions.Count);

        StatusMessage = BuildStatus();

        OnPropertyChanged(nameof(FileSummary));
        OnPropertyChanged(nameof(CanSave));
        RaiseMergeState();
    }

    private string BuildStatus()
    {
        var result = _comparison.Result;

        if (result.AreIdentical)
        {
            return "Nothing to merge - all three files agree.";
        }

        return result.ConflictCount == 0
            ? $"Merged cleanly: {result.AutoMergedCount} region(s), no conflicts."
            : $"{result.ConflictCount} conflict(s)   ·   {result.AutoMergedCount} merged automatically";
    }

    private void Reset()
    {
        _comparison = ThreeWayComparison.Empty;
        _state = ThreeWayMergeState.Empty;
        Pane.Clear();

        OnPropertyChanged(nameof(FileSummary));
        OnPropertyChanged(nameof(CanSave));
        RaiseMergeState();
    }

    private void RaiseMergeState()
    {
        OnPropertyChanged(nameof(UnresolvedConflicts));
        OnPropertyChanged(nameof(HasUnresolvedConflicts));
        OnPropertyChanged(nameof(UnresolvedDetail));
    }
}
