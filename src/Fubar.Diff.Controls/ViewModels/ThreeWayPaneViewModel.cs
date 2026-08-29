using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Controls.ViewModels;

/// <summary>
/// Everything needed to DISPLAY a three-way merge: the three flattened documents, the regions, and
/// where the user is in them. Nothing about where the content came from, or about saving - the same
/// division <see cref="DiffPaneViewModel"/> draws, so this widget can be hosted by anything that can
/// produce a <see cref="ThreeWayResult"/>.
/// </summary>
public partial class ThreeWayPaneViewModel : ObservableObject
{
    private ThreeWayResult _result = ThreeWayResult.Empty;

    /// <summary>The ancestor column - what both edits started from.</summary>
    [ObservableProperty]
    public partial AlignedDocument? BaseDocument { get; set; }

    /// <summary>The left edit's column.</summary>
    [ObservableProperty]
    public partial AlignedDocument? LeftDocument { get; set; }

    /// <summary>The right edit's column.</summary>
    [ObservableProperty]
    public partial AlignedDocument? RightDocument { get; set; }

    /// <summary>The merge being shown, for a host that needs the regions or the counts.</summary>
    public ThreeWayResult Result => _result;

    /// <summary>Total display rows - all three columns have this many.</summary>
    public int TotalLines => _result.Lines.Count;

    /// <summary>The regions, for navigation and for a host's status line.</summary>
    public IReadOnlyList<MergeRegion> Regions => _result.Regions;

    /// <summary>True when there is anything to merge at all.</summary>
    public bool HasRegions => _result.Regions.Count > 0;

    /// <summary>How many regions need a person.</summary>
    public int ConflictCount => _result.ConflictCount;

    /// <summary>How many regions the merge settled on its own.</summary>
    public int AutoMergedCount => _result.AutoMergedCount;

    /// <summary>Index into <see cref="Regions"/>, or -1 when nothing is selected yet.</summary>
    [ObservableProperty]
    public partial int CurrentRegion { get; set; } = -1;

    /// <summary>Row to bring into view. The view watches this and scrolls; -1 means nothing pending.</summary>
    [ObservableProperty]
    public partial int ScrollToRow { get; set; } = -1;

    /// <summary>Whether the editors mark invisible characters. Purely a display setting.</summary>
    [ObservableProperty]
    public partial bool ShowInvisibles { get; set; }

    /// <summary>Whether the panes colour their content by the file's own grammar.</summary>
    [ObservableProperty]
    public partial bool SyntaxHighlighting { get; set; } = true;

    /// <summary>
    /// The file extension each column is highlighted with. One value, not three: a merge's three
    /// documents are three versions of ONE file, so unlike a two-way comparison there is no sensible
    /// case where they are different languages.
    /// </summary>
    [ObservableProperty]
    public partial string? SyntaxExtension { get; set; }

    /// <summary>
    /// Whether navigation stops only on conflicts. On by default, which is the whole reason a merge is
    /// faster than a diff: the regions only one side touched are already decided, and walking through
    /// them to reach the handful that are contested is how a merge tool becomes slower than doing it by
    /// hand.
    /// </summary>
    [ObservableProperty]
    public partial bool ConflictsOnly { get; set; } = true;

    /// <summary>
    /// Whether long stretches of unchanged context are hidden. On by default, and worth more here than
    /// in a two-way diff: most of a merge's regions resolve themselves, so the reader is hunting the
    /// few that do not through the same thousands of untouched lines.
    /// </summary>
    [ObservableProperty]
    public partial bool CollapseUnchanged { get; set; } = true;

    /// <summary>How many unchanged rows stay visible either side of a region.</summary>
    [ObservableProperty]
    public partial int ContextLines { get; set; } = 3;

    /// <summary>The rows to fold. All three columns are given this same list, which keeps them aligned.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FoldRange> Folds { get; set; } = [];

    partial void OnCollapseUnchangedChanged(bool value) => RebuildFolds();

    partial void OnContextLinesChanged(int value) => RebuildFolds();

    private void RebuildFolds() =>
        Folds = CollapseUnchanged ? CollapsedRegions.Compute(_result, ContextLines) : [];

    /// <summary>True when a specific region is selected, so a host can hide its resolution controls.</summary>
    public bool HasCurrentRegion => CurrentRegion >= 0 && CurrentRegion < _result.Regions.Count;

    /// <summary>The selected region, or null.</summary>
    public MergeRegion? SelectedRegion => HasCurrentRegion ? _result.Regions[CurrentRegion] : null;

    /// <summary>Raised when navigation moves, so a host can update its own status text.</summary>
    public event EventHandler? Navigated;

    partial void OnCurrentRegionChanged(int value)
    {
        OnPropertyChanged(nameof(HasCurrentRegion));
        OnPropertyChanged(nameof(SelectedRegion));
        OnPropertyChanged(nameof(RegionCaption));
        RebuildDetail();
    }

    // ---- Close-up ---------------------------------------------------------------------------------

    /// <summary>
    /// Whether the close-up of the current region is shown below the columns. On by default, for the
    /// reason its two-way counterpart is: the three versions of one conflict are usually a screen
    /// apart vertically once a file is any size, and reading them together is the hard part.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDetailVisible { get; set; } = true;

    /// <summary>The current region's rows on the left, with fillers dropped.</summary>
    [ObservableProperty]
    public partial AlignedDocument? DetailLeft { get; set; }

    /// <summary>The current region's rows in the ancestor.</summary>
    [ObservableProperty]
    public partial AlignedDocument? DetailBase { get; set; }

    /// <summary>The current region's rows on the right.</summary>
    [ObservableProperty]
    public partial AlignedDocument? DetailRight { get; set; }

    /// <summary>True once there is something to show, so the host can swap in an empty state.</summary>
    public bool HasDetail => DetailBase is not null || DetailLeft is not null || DetailRight is not null;

    private void RebuildDetail()
    {
        if (SelectedRegion is not { } region)
        {
            DetailLeft = null;
            DetailBase = null;
            DetailRight = null;
            OnPropertyChanged(nameof(HasDetail));
            return;
        }

        DetailLeft = ThreeWayAlignedText.BuildCompact(_result, MergeSide.Left, region.StartIndex, region.Length);
        DetailBase = ThreeWayAlignedText.BuildCompact(_result, MergeSide.Base, region.StartIndex, region.Length);
        DetailRight = ThreeWayAlignedText.BuildCompact(_result, MergeSide.Right, region.StartIndex, region.Length);

        OnPropertyChanged(nameof(HasDetail));
    }

    /// <summary>
    /// Names the selected region and the lines it covers in each of the three files.
    ///
    /// All three, deliberately: a conflict is a disagreement ABOUT a place, and being told only where
    /// it lands in one of the files leaves the reader to find the other two themselves.
    /// </summary>
    public string RegionCaption
    {
        get
        {
            if (_result.Regions.Count == 0)
            {
                return "Nothing to merge - the three files agree.";
            }

            if (SelectedRegion is not { } region)
            {
                return $"{_result.ConflictCount} conflict(s), {_result.AutoMergedCount} merged automatically";
            }

            var range = MergeRegionNavigator.RangeOf(_result.Lines, region);

            return $"{Describe(region.Kind)}   ·   base {Lines(range.BaseStart, range.BaseEnd)}"
                   + $"   ·   left {Lines(range.LeftStart, range.LeftEnd)}"
                   + $"   ·   right {Lines(range.RightStart, range.RightEnd)}";
        }
    }

    private static string Describe(MergeKind kind) => kind switch
    {
        MergeKind.Conflict => "Conflict - both sides changed this",
        MergeKind.LeftOnly => "Left changed this",
        MergeKind.RightOnly => "Right changed this",
        MergeKind.BothSame => "Both sides made the same change",
        _ => "Unchanged",
    };

    /// <summary>"—" rather than a range when a file contributes no lines here at all.</summary>
    private static string Lines(int? start, int? end) => start is null || end is null
        ? "—"
        : start == end ? $"line {start}" : $"lines {start}–{end}";

    /// <summary>
    /// Shows a merge. The only way content gets in, so every host goes through the same path.
    /// </summary>
    public void Show(ThreeWayResult result)
    {
        _result = result;

        // Before the documents: the panes fold when the document arrives, so a list computed
        // afterwards would leave the first frame unfolded.
        RebuildFolds();

        BaseDocument = ThreeWayAlignedText.Build(result, MergeSide.Base);
        LeftDocument = ThreeWayAlignedText.Build(result, MergeSide.Left);
        RightDocument = ThreeWayAlignedText.Build(result, MergeSide.Right);

        CurrentRegion = -1;
        ScrollToRow = -1;

        RebuildDetail();
        RaiseDerived();
    }

    /// <summary>Clears the pane back to empty.</summary>
    public void Clear() => Show(ThreeWayResult.Empty);

    [RelayCommand]
    public void NextRegion() => MoveTo(MergeRegionNavigator.Next(_result.Regions, CurrentRegion, ConflictsOnly));

    [RelayCommand]
    public void PreviousRegion() => MoveTo(MergeRegionNavigator.Previous(_result.Regions, CurrentRegion, ConflictsOnly));

    /// <summary>Jumps to a row, syncing the region selection to match - e.g. from a click.</summary>
    public void JumpToRow(int rowIndex)
    {
        ScrollToRow = rowIndex;
        CurrentRegion = MergeRegionNavigator.IndexOfRegionContaining(_result.Regions, rowIndex);
    }

    private void MoveTo(int? regionIndex)
    {
        if (regionIndex is not { } index)
        {
            return;
        }

        CurrentRegion = index;
        ScrollToRow = _result.Regions[index].StartIndex;
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Notifies the computed properties that read through to the result. They have no setter for the
    /// generator to hook, so they must be raised by hand whenever the merge is replaced.
    /// </summary>
    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(TotalLines));
        OnPropertyChanged(nameof(Regions));
        OnPropertyChanged(nameof(HasRegions));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(AutoMergedCount));
        OnPropertyChanged(nameof(HasCurrentRegion));
        OnPropertyChanged(nameof(SelectedRegion));
        OnPropertyChanged(nameof(RegionCaption));
    }
}
