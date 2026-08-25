using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Controls.ViewModels;

/// <summary>
/// Everything needed to DISPLAY a comparison: the two flattened documents, the hunks, navigation, and
/// the semantic tree. Nothing about where the content came from.
///
/// That separation is the point. Fubar Diff loads files; API Studio hands it two in-memory strings -
/// an existing request against the one an OpenAPI spec would import, or two HTTP responses. Both get
/// the same view because both end up as a <see cref="DiffResult"/>, and this is the only thing
/// <see cref="Views.DiffView"/> binds to.
/// </summary>
public partial class DiffPaneViewModel : ObservableObject
{
    private DiffResult _result = DiffResult.Empty;

    /// <summary>The left editor's flattened document, including filler lines.</summary>
    [ObservableProperty]
    public partial AlignedDocument? LeftDocument { get; set; }

    /// <summary>The right editor's flattened document, including filler lines.</summary>
    [ObservableProperty]
    public partial AlignedDocument? RightDocument { get; set; }

    /// <summary>Total display rows - the denominator the diff map scales positions against.</summary>
    public int TotalLines => _result.Lines.Count;

    /// <summary>The hunks, for the diff map and navigation.</summary>
    public IReadOnlyList<DiffHunk> Hunks => _result.Hunks;

    /// <summary>The rows, so the diff map can colour each tick by change kind.</summary>
    public IReadOnlyList<DiffLine> Lines => _result.Lines;

    /// <summary>The comparison being shown, for hosts that need the counts or the merge model.</summary>
    public DiffResult Result => _result;

    public bool HasChanges => _result.Hunks.Count > 0;

    /// <summary>Index into the hunk list, or -1 when nothing is selected yet.</summary>
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

    /// <summary>
    /// True when a specific change is selected. Merge commands act on the CURRENT hunk, so a host
    /// binding them must disable them until one is picked, or they silently do nothing.
    /// </summary>
    public bool HasCurrentHunk => CurrentHunk >= 0 && CurrentHunk < _result.Hunks.Count;

    partial void OnCurrentHunkChanged(int value)
    {
        OnPropertyChanged(nameof(HasCurrentHunk));
        OnPropertyChanged(nameof(CurrentIgnorePath));
        OnPropertyChanged(nameof(CanIgnoreCurrent));
        OnPropertyChanged(nameof(IgnoreCurrentTooltip));
        RebuildDetail();
    }

    // ---- Detail pane ----------------------------------------------------------------------------

    /// <summary>
    /// Whether the close-up of the current difference is shown below the panes. On by default: in a
    /// long file the two sides of one change are often far enough apart vertically that reading them
    /// together is the hard part, which is the whole reason the pane exists.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDetailVisible { get; set; } = true;

    /// <summary>The current hunk's rows, left side, or null when no hunk is selected.</summary>
    [ObservableProperty]
    public partial AlignedDocument? DetailLeft { get; set; }

    /// <summary>The current hunk's rows, right side.</summary>
    [ObservableProperty]
    public partial AlignedDocument? DetailRight { get; set; }

    /// <summary>Names the current difference and the file lines it covers.</summary>
    [ObservableProperty]
    public partial string DetailCaption { get; set; } = "No difference selected";

    /// <summary>True once there is something to show, so the host can swap in an empty state.</summary>
    public bool HasDetail => DetailLeft is not null;

    partial void OnDetailLeftChanged(AlignedDocument? value) => OnPropertyChanged(nameof(HasDetail));

    private void RebuildDetail()
    {
        if (!HasCurrentHunk)
        {
            DetailLeft = null;
            DetailRight = null;
            DetailCaption = _result.Hunks.Count == 0
                ? "No differences"
                : "No difference selected";
            return;
        }

        var hunk = _result.Hunks[CurrentHunk];

        DetailLeft = AlignedText.Build(_result, DiffSide.Left, hunk.StartIndex, hunk.Length);
        DetailRight = AlignedText.Build(_result, DiffSide.Right, hunk.StartIndex, hunk.Length);

        var range = HunkNavigator.RangeOf(_result.Lines, hunk);
        DetailCaption =
            $"Difference {CurrentHunk + 1} of {_result.Hunks.Count}   ·   " +
            $"left {Describe(range.LeftStart, range.LeftEnd)}   ·   right {Describe(range.RightStart, range.RightEnd)}";
    }

    /// <summary>"added"/"removed" rather than a line range when a side contributes no lines at all.</summary>
    private static string Describe(int? start, int? end) => start is null || end is null
        ? "—"
        : start == end ? $"line {start}" : $"lines {start}–{end}";

    // ---- Ignore rules ---------------------------------------------------------------------------

    /// <summary>
    /// What to run when the user asks to ignore a path, or null when the host does not support it.
    ///
    /// Supplied by the host rather than implemented here, because ignoring means re-running the
    /// comparison, and this view model deliberately knows nothing about where its content came from.
    /// Fubar Diff leaves it null and the affordance never appears; API Studio sets it, because there
    /// a comparison belongs to a request that can remember the rule.
    /// </summary>
    [ObservableProperty]
    public partial IRelayCommand<string>? IgnorePathCommand { get; set; }

    /// <summary>Whether to offer the ignore affordance at all.</summary>
    public bool CanIgnorePaths => IgnorePathCommand is not null;

    partial void OnIgnorePathCommandChanged(IRelayCommand<string>? value)
    {
        OnPropertyChanged(nameof(CanIgnorePaths));
        OnPropertyChanged(nameof(CanIgnoreCurrent));
    }

    /// <summary>Resolves a row back to the semantic change on it, for ignoring from the text view.</summary>
    private JsonChangeIndex _changeIndex = JsonChangeIndex.Empty;

    /// <summary>
    /// The rule to create for the difference the user is currently on, or null when there is none -
    /// nothing selected, a non-JSON comparison, or a row the semantic pass never flagged.
    ///
    /// Array indices are generalized, so ignoring a field from inside one element covers every
    /// element. Identical to what the tree produces, deliberately: the two views must not create
    /// different rules for the same field.
    /// </summary>
    public string? CurrentIgnorePath
    {
        get
        {
            if (!HasCurrentHunk)
            {
                return null;
            }

            var hunk = _result.Hunks[CurrentHunk];

            for (var i = hunk.StartIndex; i <= hunk.EndIndex && i < _result.Lines.Count; i++)
            {
                var row = _result.Lines[i];
                if (_changeIndex.Find(row.LeftNumber, row.RightNumber) is { } change)
                {
                    return JsonPathPattern.Generalize(change.Path.ToString());
                }
            }

            return null;
        }
    }

    /// <summary>Whether "ignore this field" is available right now.</summary>
    public bool CanIgnoreCurrent => CanIgnorePaths && CurrentIgnorePath is not null;

    /// <summary>Describes what would be ignored, for the button's tooltip.</summary>
    public string IgnoreCurrentTooltip => CurrentIgnorePath is { } path
        ? $"Never report differences at {path}"
        : "Select a difference to ignore it";

    // ---- Semantic JSON --------------------------------------------------------------------------

    /// <summary>True when the semantic JSON pass ran, which is what enables the tree view.</summary>
    [ObservableProperty]
    public partial bool IsSemantic { get; set; }

    /// <summary>The semantic changes as a tree, for the Tree view.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<JsonChangeNodeViewModel> SemanticTree { get; set; } = [];

    /// <summary>Which view the pane shows. Only meaningful once a semantic comparison has run.</summary>
    [ObservableProperty]
    public partial DiffViewMode ViewMode { get; set; } = DiffViewMode.Text;

    /// <summary>The values offered by a view selector.</summary>
    public static IReadOnlyList<DiffViewMode> ViewModeOptions { get; } = Enum.GetValues<DiffViewMode>();

    /// <summary>Whether the side-by-side editors are the visible pane.</summary>
    public bool IsTextViewVisible => LeftDocument is not null && ViewMode == DiffViewMode.Text;

    /// <summary>
    /// Whether the change tree is the visible pane. Requires a semantic comparison - the tree would
    /// otherwise be permanently empty and look broken.
    /// </summary>
    public bool IsTreeViewVisible => LeftDocument is not null && ViewMode == DiffViewMode.Tree && IsSemantic;

    partial void OnViewModeChanged(DiffViewMode value) => RaiseViewVisibility();

    partial void OnLeftDocumentChanged(AlignedDocument? value) => RaiseViewVisibility();

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

    // ---- Loading --------------------------------------------------------------------------------

    /// <summary>
    /// Shows a comparison. The only way content gets in, so every host - file-based or in-memory -
    /// goes through the same path.
    /// </summary>
    public void Show(
        DiffResult result,
        bool isSemantic = false,
        IReadOnlyList<JsonChange>? semanticChanges = null)
    {
        _result = result;

        LeftDocument = AlignedText.Build(result, DiffSide.Left);
        RightDocument = AlignedText.Build(result, DiffSide.Right);

        IsSemantic = isSemantic;
        _changeIndex = JsonChangeIndex.Build(semanticChanges);
        SemanticTree = JsonChangeNodeViewModel.Build(semanticChanges ?? []);

        CurrentHunk = -1;
        ScrollToRow = -1;

        RebuildDetail();
        RaiseDerived();
    }

    /// <summary>Clears the pane back to empty.</summary>
    public void Clear() => Show(DiffResult.Empty);

    // ---- Navigation -----------------------------------------------------------------------------

    [RelayCommand]
    public void NextChange() => MoveTo(HunkNavigator.Next(_result.Hunks, CurrentHunk));

    [RelayCommand]
    public void PreviousChange() => MoveTo(HunkNavigator.Previous(_result.Hunks, CurrentHunk));

    /// <summary>Jumps to a row, e.g. from a diff-map click, syncing the hunk selection to match.</summary>
    public void JumpToRow(int rowIndex)
    {
        ScrollToRow = rowIndex;
        CurrentHunk = HunkNavigator.IndexOfHunkContaining(_result.Hunks, rowIndex);
    }

    /// <summary>Raised when navigation moves, so a host can update its own status text.</summary>
    public event EventHandler? Navigated;

    private void MoveTo(int? hunkIndex)
    {
        if (hunkIndex is not { } index)
        {
            return;
        }

        CurrentHunk = index;
        ScrollToRow = _result.Hunks[index].StartIndex;
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseViewVisibility()
    {
        OnPropertyChanged(nameof(IsTextViewVisible));
        OnPropertyChanged(nameof(IsTreeViewVisible));
    }

    /// <summary>
    /// Notifies the computed properties that read through to the result. They have no setter for the
    /// generator to hook, so they must be raised by hand whenever the comparison is replaced.
    /// </summary>
    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasCurrentHunk));
        OnPropertyChanged(nameof(CurrentIgnorePath));
        OnPropertyChanged(nameof(CanIgnoreCurrent));
        OnPropertyChanged(nameof(IgnoreCurrentTooltip));
        OnPropertyChanged(nameof(TotalLines));
        OnPropertyChanged(nameof(Hunks));
        OnPropertyChanged(nameof(Lines));
    }
}
