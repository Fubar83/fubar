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

    partial void OnCurrentHunkChanged(int value) => OnPropertyChanged(nameof(HasCurrentHunk));

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
        SemanticTree = JsonChangeNodeViewModel.Build(semanticChanges ?? []);

        CurrentHunk = -1;
        ScrollToRow = -1;

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
        OnPropertyChanged(nameof(TotalLines));
        OnPropertyChanged(nameof(Hunks));
        OnPropertyChanged(nameof(Lines));
    }
}
