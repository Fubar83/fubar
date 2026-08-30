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

    /// <summary>
    /// The whole comparison as ONE patch-style document, for the unified view.
    ///
    /// Its rows do not correspond to <see cref="Lines"/> one for one - a modified row becomes two, a
    /// filler becomes none - so it carries its own hunk ranges and a row mapping. See
    /// <see cref="UnifiedText"/> for why that is kept here rather than by weakening the side-by-side
    /// invariant for everybody.
    /// </summary>
    [ObservableProperty]
    public partial UnifiedDocument UnifiedDocument { get; set; } = UnifiedDocument.Empty;

    /// <summary>The unified document's own folds, computed in its own row coordinates.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FoldRange> UnifiedFolds { get; set; } = [];

    /// <summary>Row to bring into view in the UNIFIED document; -1 means nothing pending.</summary>
    [ObservableProperty]
    public partial int UnifiedScrollToRow { get; set; } = -1;

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

    /// <summary>
    /// Whether the editors mark invisible characters (NBSP, zero-width, bidi controls).
    ///
    /// Purely a display setting - it changes nothing about what the comparison found. It exists for
    /// the case where the diff is RIGHT and looks wrong: two lines that differ only by a non-breaking
    /// space are reported as different and appear identical, and there is no way to tell why without
    /// this.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowInvisibles { get; set; }

    /// <summary>
    /// The file extension each side should be syntax-highlighted with (<c>.cs</c>, <c>.ts</c>), or
    /// null for none.
    ///
    /// Per side rather than one for the comparison: comparing a file against its rewrite in another
    /// language is a real thing people do, and it is precisely the case where colouring both halves
    /// with one grammar would be most misleading.
    ///
    /// Supplied by the host, like <see cref="LeftRawText"/>, because this view model deliberately knows
    /// nothing about where its content came from - a host comparing two in-memory strings has no
    /// extension to offer and simply leaves these null.
    /// </summary>
    [ObservableProperty]
    public partial string? LeftSyntaxExtension { get; set; }

    /// <summary>The right side's extension for highlighting - see <see cref="LeftSyntaxExtension"/>.</summary>
    [ObservableProperty]
    public partial string? RightSyntaxExtension { get; set; }

    /// <summary>
    /// Whether the panes colour their content at all. On by default, and a DISPLAY setting like
    /// <see cref="ShowInvisibles"/> - it changes nothing about what the comparison found, so toggling
    /// it never re-runs a diff.
    /// </summary>
    [ObservableProperty]
    public partial bool SyntaxHighlighting { get; set; } = true;

    /// <summary>
    /// Whether long lines wrap in the UNIFIED view.
    ///
    /// Unified only, and that is a constraint rather than an oversight. The side-by-side panes rest on
    /// "editor line i is the same row in both", which is what makes scroll sync a plain offset copy;
    /// wrapping breaks it the moment one side's line is long enough to take two visual lines and the
    /// other's is not, and the columns drift apart by a line for every wrap above the viewport. The
    /// unified view has one document and nothing to keep in step with, so it can simply wrap - and it
    /// is the view people are in when the lines are too long to read anyway, which is why the option
    /// belongs to it rather than being a global that does nothing half the time.
    ///
    /// Off by default: a wrapped line has no fixed height, so a screen of diff holds fewer changes,
    /// and the reader loses the ability to scan down a column.
    /// </summary>
    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    /// <summary>
    /// Whether long stretches of unchanged context are hidden behind a collapsed placeholder.
    ///
    /// On by default, which is what every review tool does and the reason they are pleasant to read: a
    /// 3,000-line file with two changes is otherwise 3,000 lines of scrolling to see two. Nothing is
    /// hidden irrecoverably - each fold is one click, and the setting is remembered - so the usual
    /// argument against changing what the user is shown does not apply here the way it does to
    /// reformatting their content.
    /// </summary>
    [ObservableProperty]
    public partial bool CollapseUnchanged { get; set; } = true;

    /// <summary>How many unchanged rows stay visible either side of a change.</summary>
    [ObservableProperty]
    public partial int ContextLines { get; set; } = 3;

    /// <summary>
    /// The rows to fold, or empty when collapsing is off. Both panes are given this SAME list, which
    /// is what keeps them aligned - see <c>DiffEditorPane.FoldsProperty</c>.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<FoldRange> Folds { get; set; } = [];

    partial void OnCollapseUnchangedChanged(bool value) => RebuildFolds();

    partial void OnContextLinesChanged(int value) => RebuildFolds();

    private void RebuildFolds()
    {
        Folds = CollapseUnchanged ? CollapsedRegions.Compute(_result.Lines, ContextLines) : [];

        // The unified document has its own row coordinates, so its folds have to be computed against
        // its own lines rather than remapped from the side-by-side ones.
        UnifiedFolds = CollapseUnchanged
            ? CollapsedRegions.Compute(UnifiedDocument.Document.Lines, ContextLines)
            : [];
    }

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

        // Compact, not the fillers-included Build: the detail pane stacks old above new rather than
        // side by side, so there is no row-count parity to preserve, and a filler would only insert
        // a pointless blank line into what should read as one coherent block per side.
        DetailLeft = AlignedText.BuildCompact(_result, DiffSide.Left, hunk.StartIndex, hunk.Length);
        DetailRight = AlignedText.BuildCompact(_result, DiffSide.Right, hunk.StartIndex, hunk.Length);

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

    /// <summary>True when the semantic JSON pass ran, which is what enables the Json view.</summary>
    [ObservableProperty]
    public partial bool IsSemantic { get; set; }

    /// <summary>The semantic changes as a tree, embedded in the Json view.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<JsonChangeNodeViewModel> SemanticTree { get; set; } = [];

    /// <summary>Looks up a change's tree row without re-walking the tree - see <see cref="JsonChangeNodeViewModel.Build"/>.</summary>
    private IReadOnlyDictionary<string, JsonChangeNodeViewModel> _treeNodesByPath =
        new Dictionary<string, JsonChangeNodeViewModel>();

    /// <summary>
    /// Which view the pane shows. Defaults to side by side, but <see cref="Show"/> resets it on every
    /// comparison - Json when semantic comparison ran, side by side otherwise - so it is never left
    /// showing Json for a document that turned out not to be JSON, and JSON always opens in the view
    /// built for it rather than requiring a manual switch every time.
    /// </summary>
    [ObservableProperty]
    public partial DiffViewMode ViewMode { get; set; } = DiffViewMode.SideBySide;

    /// <summary>Every view mode that exists.</summary>
    public static IReadOnlyList<DiffViewMode> ViewModeOptions { get; } = Enum.GetValues<DiffViewMode>();

    /// <summary>
    /// The modes offered for THIS comparison. Side by side and unified always apply; Json is dropped
    /// for content that is not JSON rather than offered and then refusing to show anything, which is
    /// how the selector used to hide itself entirely - it no longer can, because the other two are
    /// always a real choice.
    /// </summary>
    public IReadOnlyList<DiffViewMode> AvailableViewModes => IsSemantic
        ? ViewModeOptions
        : [DiffViewMode.SideBySide, DiffViewMode.Unified];

    /// <summary>Whether the side-by-side editors are the visible pane.</summary>
    public bool IsSideBySideViewVisible => LeftDocument is not null && ViewMode == DiffViewMode.SideBySide;

    /// <summary>Whether the single-document patch view is the visible pane.</summary>
    public bool IsUnifiedViewVisible => LeftDocument is not null && ViewMode == DiffViewMode.Unified;

    /// <summary>
    /// Whether the tree-plus-both-documents view is the visible pane. Requires a semantic comparison -
    /// it would otherwise show an empty tree next to two documents with nothing to highlight.
    /// </summary>
    public bool IsJsonViewVisible => LeftDocument is not null && ViewMode == DiffViewMode.Json && IsSemantic;

    partial void OnViewModeChanged(DiffViewMode value)
    {
        // The close-up exists to put a change's two versions next to each other; in the unified view
        // they already ARE next to each other, one line apart, so it would be showing a copy of what is
        // on screen. Hidden rather than merely empty, and remembered, so switching back restores it.
        if (value == DiffViewMode.Unified)
        {
            _detailBeforeUnified = IsDetailVisible;
            IsDetailVisible = false;
        }
        else if (_detailBeforeUnified is { } previous)
        {
            IsDetailVisible = previous;
            _detailBeforeUnified = null;
        }

        RaiseViewVisibility();
    }

    /// <summary>What the close-up's visibility was before the unified view turned it off.</summary>
    private bool? _detailBeforeUnified;

    partial void OnLeftDocumentChanged(AlignedDocument? value) => RaiseViewVisibility();

    partial void OnIsSemanticChanged(bool value)
    {
        // Safety net for IsSemantic changing outside Show (it currently never does, but Show already
        // sets ViewMode explicitly for every comparison, so this only matters if that ever changes):
        // leaving Json selected for content that is not semantic would show an empty pane.
        if (!value && ViewMode == DiffViewMode.Json)
        {
            ViewMode = DiffViewMode.SideBySide;
        }

        RaiseViewVisibility();
    }

    // ---- Json view --------------------------------------------------------------------------------

    /// <summary>
    /// Each side's own document text, unaligned - exactly what the semantic pass parsed, so every
    /// <see cref="JsonChange"/>'s <see cref="SourceSpan"/> addresses it directly. Supplied by the host
    /// alongside the comparison, since building it is just joining the lines it already has.
    /// </summary>
    [ObservableProperty]
    public partial string LeftRawText { get; set; } = string.Empty;

    /// <summary>The right side's own document text, unaligned.</summary>
    [ObservableProperty]
    public partial string RightRawText { get; set; } = string.Empty;

    /// <summary>The full, flat change list Json-view navigation walks - document order, ignored included.</summary>
    private IReadOnlyList<JsonChange> _semanticChanges = [];

    /// <summary>Index into <see cref="_semanticChanges"/>, or -1 when nothing is selected.</summary>
    [ObservableProperty]
    public partial int CurrentSemanticChangeIndex { get; set; } = -1;

    /// <summary>
    /// The tree row for the current change, bound two-way to the embedded tree's own selection - so
    /// clicking a row in the tree navigates the Json view exactly like Prev/Next does, and stepping with
    /// Prev/Next moves the tree's selection to match.
    /// </summary>
    [ObservableProperty]
    public partial JsonChangeNodeViewModel? CurrentTreeNode { get; set; }

    /// <summary>The change the Json view is currently showing, or null when nothing is selected.</summary>
    public JsonChange? CurrentSemanticChange =>
        CurrentSemanticChangeIndex >= 0 && CurrentSemanticChangeIndex < _semanticChanges.Count
            ? _semanticChanges[CurrentSemanticChangeIndex]
            : null;

    /// <summary>Where to highlight on the left - the change's own span into <see cref="LeftRawText"/>.</summary>
    public SourceSpan? LeftHighlightSpan => CurrentSemanticChange?.Left?.Span is { IsKnown: true } span ? span : null;

    /// <summary>Where to highlight on the right.</summary>
    public SourceSpan? RightHighlightSpan => CurrentSemanticChange?.Right?.Span is { IsKnown: true } span ? span : null;

    // ---- Json detail (close-up) ------------------------------------------------------------------

    /// <summary>
    /// The Json view's counterpart to <see cref="DetailLeft"/>: the current change's own lines on the
    /// left, isolated from the rest of the document via <see cref="JsonSpanExcerpt"/> rather than
    /// excerpted from a hunk's aligned rows - Json changes have no aligned rows to excerpt from (see
    /// "The Json view has no alignment at all, on purpose"). Empty when the left side has no node for
    /// this change (a pure insertion).
    /// </summary>
    [ObservableProperty]
    public partial string DetailLeftRawText { get; set; } = string.Empty;

    /// <summary>Where to highlight within <see cref="DetailLeftRawText"/> - the same span, renumbered to the excerpt.</summary>
    [ObservableProperty]
    public partial SourceSpan? DetailLeftHighlightSpan { get; set; }

    /// <summary>The current change's own lines on the right.</summary>
    [ObservableProperty]
    public partial string DetailRightRawText { get; set; } = string.Empty;

    /// <summary>Where to highlight within <see cref="DetailRightRawText"/>.</summary>
    [ObservableProperty]
    public partial SourceSpan? DetailRightHighlightSpan { get; set; }

    private void RebuildJsonDetail()
    {
        (DetailLeftRawText, DetailLeftHighlightSpan) = BuildJsonExcerpt(LeftRawText, LeftHighlightSpan);
        (DetailRightRawText, DetailRightHighlightSpan) = BuildJsonExcerpt(RightRawText, RightHighlightSpan);
    }

    private static (string Text, SourceSpan? Span) BuildJsonExcerpt(string rawText, SourceSpan? span)
    {
        if (span is not { } known)
        {
            return (string.Empty, null);
        }

        var (text, excerptSpan) = JsonSpanExcerpt.Build(rawText, known);
        return (text, excerptSpan);
    }

    /// <summary>Names the current change for the Json view's toolbar - path, kind, and position in the list.</summary>
    public string JsonCaption
    {
        get
        {
            var navigableCount = 0;
            foreach (var change in _semanticChanges)
            {
                if (!change.IsIgnored)
                {
                    navigableCount++;
                }
            }

            if (navigableCount == 0)
            {
                return "No differences";
            }

            if (CurrentSemanticChange is not { } current)
            {
                return $"{navigableCount} difference(s) - none selected";
            }

            var position = 0;
            for (var i = 0; i <= CurrentSemanticChangeIndex; i++)
            {
                if (!_semanticChanges[i].IsIgnored)
                {
                    position++;
                }
            }

            return $"{current.Path}   ·   {current.Kind}   ·   {position} of {navigableCount}";
        }
    }

    [RelayCommand]
    private void NextSemanticChange() => MoveToSemanticChange(SemanticChangeNavigator.Next(_semanticChanges, CurrentSemanticChangeIndex));

    [RelayCommand]
    private void PreviousSemanticChange() => MoveToSemanticChange(SemanticChangeNavigator.Previous(_semanticChanges, CurrentSemanticChangeIndex));

    private void MoveToSemanticChange(int? index)
    {
        if (index is not { } value)
        {
            return;
        }

        CurrentSemanticChangeIndex = value;
    }

    partial void OnCurrentSemanticChangeIndexChanged(int value)
    {
        // Keeps the embedded tree's selection following Prev/Next. Guarded against feedback: setting
        // CurrentTreeNode here raises ITS OWN changed handler below, which would otherwise call back
        // into SetCurrentChangeIndex and re-enter this property's setter.
        var node = CurrentSemanticChange is { } change && _treeNodesByPath.TryGetValue(change.Path.ToString(), out var found)
            ? found
            : null;

        if (!ReferenceEquals(CurrentTreeNode, node))
        {
            CurrentTreeNode = node;
        }

        RaiseJsonDerived();
    }

    partial void OnCurrentTreeNodeChanged(JsonChangeNodeViewModel? value)
    {
        // The reverse direction: the user clicked a row in the tree. Only rows that ARE a change carry
        // one - clicking a grouping row like `items` selects nothing, rather than jumping somewhere
        // arbitrary within it.
        if (value?.Change is not { } change)
        {
            return;
        }

        var index = IndexOfPath(change.Path);
        if (index >= 0 && index != CurrentSemanticChangeIndex)
        {
            CurrentSemanticChangeIndex = index;
        }
    }

    /// <summary>
    /// Matched by PATH, not by the change object itself: the tree is built from the canonicalized
    /// (aligned/display) change list, while <see cref="_semanticChanges"/> - what this searches - is
    /// the ORIGINAL-text change list, so the two sides of this lookup carry different spans for what
    /// is otherwise the same logical change. The path is the one thing guaranteed identical between
    /// the two representations, since canonicalizing never reorders or renames anything.
    /// </summary>
    private int IndexOfPath(JsonPath path)
    {
        var key = path.ToString();

        for (var i = 0; i < _semanticChanges.Count; i++)
        {
            if (_semanticChanges[i].Path.ToString() == key)
            {
                return i;
            }
        }

        return -1;
    }

    private void RaiseJsonDerived()
    {
        OnPropertyChanged(nameof(CurrentSemanticChange));
        OnPropertyChanged(nameof(LeftHighlightSpan));
        OnPropertyChanged(nameof(RightHighlightSpan));
        OnPropertyChanged(nameof(JsonCaption));
        RebuildJsonDetail();
    }

    // ---- Loading --------------------------------------------------------------------------------

    /// <summary>
    /// Shows a comparison. The only way content gets in, so every host - file-based or in-memory -
    /// goes through the same path.
    /// </summary>
    /// <param name="originalSemanticChanges">
    /// The same logical changes as <paramref name="semanticChanges"/>, but with spans into each side's
    /// text exactly as given rather than the canonicalized copy used for alignment - what the Json
    /// view highlights from. Defaults to <paramref name="semanticChanges"/> when not supplied, which
    /// is only wrong for a host that canonicalizes before calling Show at all; every current host has
    /// this available (the Application layer's <c>FileComparison.OriginalSemanticChanges</c>) and
    /// passes it through.
    /// </param>
    public void Show(
        DiffResult result,
        bool isSemantic = false,
        IReadOnlyList<JsonChange>? semanticChanges = null,
        string? leftRawText = null,
        string? rightRawText = null,
        IReadOnlyList<JsonChange>? originalSemanticChanges = null)
    {
        _result = result;

        // Before the documents: the panes apply their folds when the document arrives, so a list
        // computed afterwards would leave the first frame unfolded.
        RebuildFolds();

        LeftDocument = AlignedText.Build(result, DiffSide.Left);
        RightDocument = AlignedText.Build(result, DiffSide.Right);

        UnifiedDocument = UnifiedText.Build(result);
        UnifiedFolds = CollapseUnchanged
            ? CollapsedRegions.Compute(UnifiedDocument.Document.Lines, ContextLines)
            : [];

        IsSemantic = isSemantic;
        _changeIndex = JsonChangeIndex.Build(semanticChanges);

        // The tree (paths, kinds, ignore status) is identical either way, so it is built from the
        // canonicalized list like everything else in Text mode; only navigation/highlighting needs
        // spans into the ORIGINAL text.
        var (roots, byPath) = JsonChangeNodeViewModel.Build(semanticChanges ?? []);
        SemanticTree = roots;
        _treeNodesByPath = byPath;
        _semanticChanges = originalSemanticChanges ?? semanticChanges ?? [];

        LeftRawText = leftRawText ?? string.Empty;
        RightRawText = rightRawText ?? string.Empty;

        CurrentHunk = -1;
        ScrollToRow = -1;
        CurrentSemanticChangeIndex = -1;
        CurrentTreeNode = null;

        // JSON opens in the view built for it rather than requiring a manual switch every time; a
        // non-JSON comparison lands on the one view that always works. This runs on every comparison,
        // not just the first, so re-comparing content that changed format (e.g. toggling Mode) cannot
        // leave Json selected for a document that no longer parses as JSON.
        ViewMode = isSemantic ? DiffViewMode.Json : DiffViewMode.SideBySide;

        RebuildDetail();
        RaiseDerived();
        RaiseJsonDerived();
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

        // The unified view is scrolled by its OWN row index, which is a different number for the same
        // hunk. Both are set unconditionally so switching view mid-navigation lands in the right place
        // in whichever one is showing.
        UnifiedScrollToRow = index < UnifiedDocument.Hunks.Count
            ? UnifiedDocument.Hunks[index].StartIndex
            : -1;

        Navigated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Jumps from a row of the UNIFIED document, e.g. a click in that view. Translated through the
    /// document's own row mapping, since its indices are not the comparison's.
    /// </summary>
    public void JumpToUnifiedRow(int unifiedRow)
    {
        if (unifiedRow < 0 || unifiedRow >= UnifiedDocument.SourceRows.Count)
        {
            return;
        }

        JumpToRow(UnifiedDocument.SourceRows[unifiedRow]);
        UnifiedScrollToRow = unifiedRow;
    }

    private void RaiseViewVisibility()
    {
        OnPropertyChanged(nameof(IsSideBySideViewVisible));
        OnPropertyChanged(nameof(IsUnifiedViewVisible));
        OnPropertyChanged(nameof(IsJsonViewVisible));
        OnPropertyChanged(nameof(AvailableViewModes));
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
