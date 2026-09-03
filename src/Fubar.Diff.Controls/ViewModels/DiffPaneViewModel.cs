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

    /// <summary>
    /// First row of the ignored run currently stepped to, or -1.
    ///
    /// A run of ignored rows is a place Shift+Alt+Up/Down can stop, and it is NOT a hunk - it forms none
    /// by design. So "which difference am I on" needs somewhere to live that is not
    /// <see cref="CurrentHunk"/>, or stepping onto one would mark whichever hunk happened to be selected
    /// before. The two are mutually exclusive and each is cleared when the other is set.
    /// </summary>
    [ObservableProperty]
    public partial int CurrentIgnoredRow { get; set; } = -1;

    /// <summary>Last row of that run, inclusive. Meaningless while <see cref="CurrentIgnoredRow"/> is -1.</summary>
    [ObservableProperty]
    public partial int CurrentIgnoredRowEnd { get; set; } = -1;

    /// <summary>
    /// The row the "current difference" starts at, whichever kind it is - which is what stepping reads
    /// to know where it is, so that a click, the map or the tree moving the selection moves stepping too.
    /// </summary>
    public int CurrentStopRow => HasCurrentHunk
        ? _result.Hunks[CurrentHunk].StartIndex
        : CurrentIgnoredRow;

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

    /// <summary>
    /// The close-up follows an ignored run just as it follows a hunk.
    ///
    /// Only the START is hooked: <see cref="MoveToStop"/> sets the end first so it is already right when
    /// this fires, and hooking both would rebuild the pane twice for every step - once with a stale end.
    /// </summary>
    partial void OnCurrentIgnoredRowChanged(int value) => RebuildDetail();

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
    /// Whether the two side-by-side panes can be typed into.
    ///
    /// Off by default, and off for every host that is not the diff app: API Studio compares things
    /// that are not files - a request against what an OpenAPI spec would import, two response bodies -
    /// and there is nowhere for an edit to go.
    ///
    /// Only the side-by-side view honours it. The unified view has its own row coordinates, the Json
    /// view shows each side unaligned, the close-ups show an excerpt, and a hex view of a binary file
    /// is not text that can be written back - none of those can accept an edit and none of them offer
    /// to.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEditable { get; set; }

    /// <summary>
    /// Raised when the user has typed into one of the panes. Carries the side, not the text: reading
    /// the text back costs a pass over the document, and this fires on every keystroke.
    /// </summary>
    public event EventHandler<DiffSide>? SideEdited;

    /// <summary>
    /// Reads one side's current content as the FILE's own lines, or null before a view has offered to.
    ///
    /// Handed over by the view rather than computed here, for the same reason the folder window pushes
    /// its selection up: the document, its fillers and the anchors tracking them belong to the editor,
    /// and a view model that reached for them would be reaching for a control.
    /// </summary>
    public Func<DiffSide, IReadOnlyList<string>>? FileLinesReader { get; set; }

    /// <summary>
    /// Where each side's caret is, as a FILE line number, or null when it is on a filler. Handed over
    /// by the view for the same reason as <see cref="FileLinesReader"/>.
    ///
    /// What a host does with it: aligning two lines by hand needs to know which two lines the user is
    /// pointing at, and the only coordinate that means the same thing to a comparison as it does to a
    /// pane is the file's own numbering.
    /// </summary>
    public Func<DiffSide, int?>? CaretLineReader { get; set; }

    /// <summary>Called by the view when the user edits a pane.</summary>
    public void ReportEdit(DiffSide side) => SideEdited?.Invoke(this, side);

    /// <summary>
    /// Replaces a row range in one pane, as an ordinary edit - how taking a side is applied.
    ///
    /// Handed over by the view for the same reason <see cref="FileLinesReader"/> is: the document and
    /// its undo stack belong to the editor. Null until a view offers one, and null forever in a host
    /// that does not edit.
    /// </summary>
    public Action<DiffSide, int, int, IReadOnlyList<string>>? RowReplacer { get; set; }

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
        // An ignored run is a difference you can be ON - Shift+Alt+Up/Down stops there - and it forms no
        // hunk, so the close-up used to answer "No difference selected" about something the reader had
        // just deliberately navigated to. It gets the same treatment as any other difference: both
        // sides, stacked, with its lines named. Saying which rule is hiding it is not something this can
        // know - the row records that it was equalised, not by what - so the caption says what it is.
        if (!HasCurrentHunk && CurrentIgnoredRow >= 0)
        {
            var length = CurrentIgnoredRowEnd - CurrentIgnoredRow + 1;

            DetailLeft = AlignedText.BuildCompact(_result, DiffSide.Left, CurrentIgnoredRow, length);
            DetailRight = AlignedText.BuildCompact(_result, DiffSide.Right, CurrentIgnoredRow, length);

            var ignoredRange = HunkNavigator.RangeOf(
                _result.Lines, new DiffHunk(CurrentIgnoredRow, CurrentIgnoredRowEnd));

            DetailCaption =
                $"Ignored difference   ·   " +
                $"left {Describe(ignoredRange.LeftStart, ignoredRange.LeftEnd)}   ·   " +
                $"right {Describe(ignoredRange.RightStart, ignoredRange.RightEnd)}";
            return;
        }

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

        // A MOVE is the one difference whose two halves are not on the same rows. The block left the
        // file at one place and turned up at another, so those are two hunks - and building both sides
        // of the close-up from ONE of them showed the block on one side and an empty box on the other,
        // which is the one comparison a move actually needs. Each side is therefore sourced from its own
        // end: the block where it was, beside the block where it is. Whichever end you clicked.
        //
        // The two stay two DIFFERENCES - navigation still stops at each, and the counts are unchanged.
        // This is only about what the close-up is looking at.
        var leftSource = MovedCounterpart(hunk, DiffSide.Left) ?? (hunk.StartIndex, hunk.Length);
        var rightSource = MovedCounterpart(hunk, DiffSide.Right) ?? (hunk.StartIndex, hunk.Length);

        // Compact, not the fillers-included Build: the detail pane stacks old above new rather than
        // side by side, so there is no row-count parity to preserve, and a filler would only insert
        // a pointless blank line into what should read as one coherent block per side.
        DetailLeft = AlignedText.BuildCompact(_result, DiffSide.Left, leftSource.Item1, leftSource.Item2);
        DetailRight = AlignedText.BuildCompact(_result, DiffSide.Right, rightSource.Item1, rightSource.Item2);

        var leftRange = HunkNavigator.RangeOf(_result.Lines, new DiffHunk(leftSource.Item1, leftSource.Item1 + leftSource.Item2 - 1));
        var rightRange = HunkNavigator.RangeOf(_result.Lines, new DiffHunk(rightSource.Item1, rightSource.Item1 + rightSource.Item2 - 1));
        var moved = leftSource != rightSource;

        DetailCaption =
            $"Difference {CurrentHunk + 1} of {_result.Hunks.Count}{(moved ? "   ·   moved" : string.Empty)}   ·   " +
            $"left {Describe(leftRange.LeftStart, leftRange.LeftEnd)}   ·   right {Describe(rightRange.RightStart, rightRange.RightEnd)}";
    }

    /// <summary>
    /// Where this side of a MOVED block lives, when the current hunk holds only the other end of it.
    ///
    /// Null whenever the hunk is not one end of a move, or already has content on this side - in which
    /// case the hunk's own rows are what to show, exactly as before. Returning the counterpart's rows is
    /// what lets the close-up put the block where it WAS beside the block where it IS.
    /// </summary>
    private (int Start, int Length)? MovedCounterpart(DiffHunk hunk, DiffSide side)
    {
        var lines = _result.Lines;
        var last = Math.Min(hunk.EndIndex, lines.Count - 1);

        // Already has text on this side: nothing to go looking for.
        for (var row = Math.Max(hunk.StartIndex, 0); row <= last; row++)
        {
            if ((side == DiffSide.Left ? lines[row].LeftText : lines[row].RightText) is not null)
            {
                return null;
            }
        }

        // The move id carried by the OTHER side of these rows - the end we do have.
        int? moveId = null;
        for (var row = Math.Max(hunk.StartIndex, 0); row <= last && moveId is null; row++)
        {
            moveId = side == DiffSide.Left ? lines[row].RightMoveId : lines[row].LeftMoveId;
        }

        if (moveId is not { } id)
        {
            return null;
        }

        // Where the same id appears on the side being asked about.
        var start = -1;
        var end = -1;

        for (var row = 0; row < lines.Count; row++)
        {
            var here = side == DiffSide.Left ? lines[row].LeftMoveId : lines[row].RightMoveId;
            if (here != id)
            {
                continue;
            }

            if (start < 0)
            {
                start = row;
            }

            end = row;
        }

        return start < 0 ? null : (start, end - start + 1);
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

    /// <summary>Both layouts always apply, so this is simply every mode there is.</summary>
    public IReadOnlyList<DiffViewMode> AvailableViewModes => ViewModeOptions;

    /// <summary>
    /// Whether the side-by-side editors are the visible pane.
    ///
    /// Not while a semantic comparison is showing: the Json view IS the answer for JSON, and the way
    /// to see JSON as two columns of text is to compare it as text, which the Auto/Text/Json selector
    /// is for.
    /// </summary>
    public bool IsSideBySideViewVisible =>
        LeftDocument is not null && !IsSemantic && ViewMode == DiffViewMode.SideBySide;

    /// <summary>Whether the single-document patch view is the visible pane.</summary>
    public bool IsUnifiedViewVisible =>
        LeftDocument is not null && !IsSemantic && ViewMode == DiffViewMode.Unified;

    /// <summary>
    /// Whether the tree-plus-both-documents view is the visible pane - which is exactly when a
    /// semantic comparison ran. There is nothing to choose here any more: a JSON comparison shows the
    /// Json view, and anything else shows text.
    /// </summary>
    public bool IsJsonViewVisible => LeftDocument is not null && IsSemantic;

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

    // Whether the Json view is showing is now a consequence of this alone, so there is nothing to
    // correct here - only the visibilities to re-raise.
    partial void OnIsSemanticChanged(bool value) => RaiseViewVisibility();

    // ---- Array matching ---------------------------------------------------------------------------

    /// <summary>
    /// What each array in the comparison could be matched by, keyed by JSON path. Supplied by the host
    /// BEFORE <see cref="Show"/>, like the syntax extensions - the tree is annotated as it is built.
    ///
    /// Empty in a host that cannot act on the choice, which leaves the right-click menu off.
    /// </summary>
    public IReadOnlyDictionary<string, ArrayKeyChoices> ArrayKeys { get; set; } =
        new Dictionary<string, ArrayKeyChoices>();

    /// <summary>
    /// Raised when the user picks how an array should be matched. The host owns the comparison
    /// options, so it applies the choice and re-compares; this view model only asks.
    /// </summary>
    public event EventHandler<ArrayKeyOption>? ArrayKeyChosen;

    /// <summary>Raised when the user asks to name a field the menu did not offer.</summary>
    public event EventHandler<string>? CustomArrayKeyRequested;

    [RelayCommand]
    private void ChooseArrayKey(ArrayKeyOption? option)
    {
        if (option is not null)
        {
            ArrayKeyChosen?.Invoke(this, option);
        }
    }

    [RelayCommand]
    private void RequestCustomArrayKey(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            CustomArrayKeyRequested?.Invoke(this, path);
        }
    }

    // ---- Json formatting --------------------------------------------------------------------------

    /// <summary>
    /// Whether the host can re-lay-out a document for reading. False leaves the pretty buttons hidden.
    ///
    /// Asked of the host rather than assumed, for the same reason <see cref="CanIgnorePaths"/> is:
    /// re-deriving the change spans against reformatted text needs a parser, which this view model
    /// does not have and should not acquire. A host that cannot do it does not offer it.
    /// </summary>
    [ObservableProperty]
    public partial bool CanReformat { get; set; }

    /// <summary>Whether the left document is shown pretty-printed rather than as it is on disk.</summary>
    [ObservableProperty]
    public partial bool PrettyLeft { get; set; }

    /// <summary>Whether the right document is shown pretty-printed.</summary>
    [ObservableProperty]
    public partial bool PrettyRight { get; set; }

    /// <summary>
    /// Raised when a pretty toggle moved, so the host can supply the reformatted text AND the change
    /// spans that go with it. The two must arrive together - see <c>JsonDisplay</c>.
    /// </summary>
    public event EventHandler? FormattingChanged;

    partial void OnPrettyLeftChanged(bool value) => FormattingChanged?.Invoke(this, EventArgs.Empty);

    partial void OnPrettyRightChanged(bool value) => FormattingChanged?.Invoke(this, EventArgs.Empty);

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

    /// <summary>
    /// Every semantic change, for the panes that mark all of them rather than only the current one.
    ///
    /// These are the ORIGINAL changes - spans into each side's text exactly as given, which is what
    /// the Json panes display. The other list (spans into the canonicalized copy the aligner worked
    /// on) would be a line or two out the moment a user turned on "Reformat for display".
    /// </summary>
    public IReadOnlyList<JsonChange> SemanticChanges => _semanticChanges;

    /// <summary>The change the Json view is currently showing, or null when nothing is selected.</summary>
    public JsonChange? CurrentSemanticChange =>
        CurrentSemanticChangeIndex >= 0 && CurrentSemanticChangeIndex < _semanticChanges.Count
            ? _semanticChanges[CurrentSemanticChangeIndex]
            : null;

    /// <summary>
    /// Where to highlight on the left - everything the change covers in <see cref="LeftRawText"/>.
    ///
    /// <c>JsonChange.LeftSpan</c> rather than the value's own span, which is what this used to be: for
    /// a property that was added or removed the KEY is part of what changed, and highlighting only the
    /// value left the name beside it looking untouched.
    /// </summary>
    public SourceSpan? LeftHighlightSpan => CurrentSemanticChange?.LeftSpan is { IsKnown: true } span ? span : null;

    /// <summary>Where to highlight on the right.</summary>
    public SourceSpan? RightHighlightSpan => CurrentSemanticChange?.RightSpan is { IsKnown: true } span ? span : null;

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

    /// <summary>
    /// Whether the left side of the close-up has anything in it.
    ///
    /// An inserted value exists only on the right and a deleted one only on the left, so one of these is
    /// false for a large share of the changes anyone navigates to. The close-up used to split its height
    /// evenly regardless, which spent half the pane on an empty box beside the half that had the thing
    /// the reader was looking at - worst of all on a minified document, where the side WITH content
    /// needs every pixel it can get.
    /// </summary>
    public bool HasDetailLeft => !string.IsNullOrWhiteSpace(DetailLeftRawText);

    /// <summary>Whether the right side of the close-up has anything in it. See
    /// <see cref="HasDetailLeft"/>.</summary>
    public bool HasDetailRight => !string.IsNullOrWhiteSpace(DetailRightRawText);

    /// <summary>Where to highlight within <see cref="DetailRightRawText"/>.</summary>
    [ObservableProperty]
    public partial SourceSpan? DetailRightHighlightSpan { get; set; }

    private void RebuildJsonDetail()
    {
        (DetailLeftRawText, DetailLeftHighlightSpan) = BuildJsonExcerpt(LeftRawText, LeftHighlightSpan);
        (DetailRightRawText, DetailRightHighlightSpan) = BuildJsonExcerpt(RightRawText, RightHighlightSpan);

        OnPropertyChanged(nameof(HasDetailLeft));
        OnPropertyChanged(nameof(HasDetailRight));
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

        // Open the way to it. Selecting a row inside collapsed ancestors is a selection nobody can see,
        // which is what stepping through differences looked like: the tree agreed it had moved and
        // showed nothing. Done after the assignment so the row exists to be revealed.
        node?.Reveal();

        RaiseJsonDerived();
    }

    /// <summary>
    /// Makes the difference at a row the current one, because the user clicked there.
    ///
    /// <para>The panes were read-only as a navigation surface: you could see every difference and step
    /// through them only with the toolbar, so pointing at the one you were already reading and saying
    /// "this one" was impossible. It is the obvious gesture and it was the missing half of the map,
    /// the tree and Prev/Next all agreeing about a current difference nobody could SET by hand.</para>
    ///
    /// <para>A click on unchanged text selects nothing rather than jumping to the nearest difference:
    /// the caret moves for all sorts of reasons - selecting text to copy, clicking to read - and having
    /// the window scroll somewhere else because of it would make the panes unusable for their actual
    /// job. Both halves are updated where they exist, so the Json view's change and the text view's
    /// hunk cannot disagree about which one is current.</para>
    /// </summary>
    public void SelectDifferenceAtRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _result.Lines.Count)
        {
            return;
        }

        var hunk = HunkNavigator.IndexOfHunkContaining(_result.Hunks, rowIndex);
        if (hunk >= 0 && hunk != CurrentHunk)
        {
            CurrentHunk = hunk;
            CurrentIgnoredRow = -1;
            CurrentIgnoredRowEnd = -1;
        }
        else if (hunk < 0 && _result.Lines[rowIndex].IsIgnored)
        {
            // Clicking an ignored row selected nothing at all, because no hunk contains one. Pointing at
            // a difference and saying "this one" should work for the differences a rule is hiding as
            // much as for the rest - they are the ones you most want a close-up of, since the pane is
            // the only place that says what is actually different about them.
            SelectIgnoredRunAt(rowIndex);
        }

        // In the Json view the current difference is a semantic change, not a hunk. Both are set when
        // both apply, so whichever view is on screen agrees with the other.
        var row = _result.Lines[rowIndex];
        if (_changeIndex.Find(row.LeftNumber, row.RightNumber) is { } change)
        {
            var index = IndexOfPath(change.Path);
            if (index >= 0 && index != CurrentSemanticChangeIndex)
            {
                CurrentSemanticChangeIndex = index;
            }
        }
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
        OnPropertyChanged(nameof(SemanticChanges));
        OnPropertyChanged(nameof(CurrentSemanticChange));
        OnPropertyChanged(nameof(HasDifferences));
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
        var (roots, byPath) = JsonChangeNodeViewModel.Build(semanticChanges ?? [], ArrayKeys);
        SemanticTree = roots;
        _treeNodesByPath = byPath;
        _semanticChanges = originalSemanticChanges ?? semanticChanges ?? [];

        LeftRawText = leftRawText ?? string.Empty;
        RightRawText = rightRawText ?? string.Empty;

        CurrentHunk = -1;
        ScrollToRow = -1;
        CurrentSemanticChangeIndex = -1;
        CurrentTreeNode = null;

        // ViewMode is deliberately NOT reset here any more. It only chooses between two text layouts
        // now, and someone who prefers unified prefers it for the next comparison too - resetting it
        // per comparison was only ever needed to stop Json being selected for content that is not
        // JSON, which is no longer possible.
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

    /// <summary>
    /// Next difference, whichever kind of difference this view is showing: a hunk in the text views, a
    /// semantic change in the Json one.
    ///
    /// The two really are different things - a hunk is a run of rows the aligner paired up, a semantic
    /// change is one value that differs - which is why the Json view used to bring its own Prev/Next
    /// buttons and the host hid its own to avoid two "next" buttons that disagreed. Deciding here
    /// instead leaves one pair of buttons in one place, and moves the choice to the only object that
    /// knows which view is on screen.
    /// </summary>
    [RelayCommand]
    public void NextDifference()
    {
        if (IsJsonViewVisible)
        {
            NextSemanticChange();
        }
        else
        {
            NextChange();
        }
    }

    [RelayCommand]
    public void PreviousDifference()
    {
        if (IsJsonViewVisible)
        {
            PreviousSemanticChange();
        }
        else
        {
            PreviousChange();
        }
    }

    /// <summary>
    /// Next difference INCLUDING the ignored ones - Shift+Alt+Down.
    ///
    /// Ordinary Prev/Next steps past anything a rule covers, which is the whole point of having rules.
    /// This is the other question, asked right after adding one and once more before trusting the diff:
    /// what exactly am I not being told? Without it an ignored difference is a faint mark you have to
    /// find by scrolling, which on a long file means not finding it.
    /// </summary>
    [RelayCommand]
    public void NextDifferenceIncludingIgnored()
    {
        if (IsJsonViewVisible)
        {
            MoveToSemanticChange(
                SemanticChangeNavigator.Next(_semanticChanges, CurrentSemanticChangeIndex, includeIgnored: true));
        }
        else
        {
            MoveToStop(DifferenceStops.Next(DifferenceStops.All(_result.Lines, _result.Hunks), CurrentStopRow));
        }
    }

    /// <summary>Previous difference including the ignored ones - Shift+Alt+Up.</summary>
    [RelayCommand]
    public void PreviousDifferenceIncludingIgnored()
    {
        if (IsJsonViewVisible)
        {
            MoveToSemanticChange(
                SemanticChangeNavigator.Previous(_semanticChanges, CurrentSemanticChangeIndex, includeIgnored: true));
        }
        else
        {
            MoveToStop(DifferenceStops.Previous(DifferenceStops.All(_result.Lines, _result.Hunks), CurrentStopRow));
        }
    }

    /// <summary>
    /// Selects the whole run of ignored rows containing <paramref name="rowIndex"/>.
    ///
    /// The run, not the row: a block whose indentation changed is one difference, and selecting one line
    /// of it would show a close-up of one line of a block - which is exactly the reading the grouping
    /// everywhere else exists to prevent.
    /// </summary>
    private void SelectIgnoredRunAt(int rowIndex)
    {
        var lines = _result.Lines;
        var start = rowIndex;
        var end = rowIndex;

        while (start > 0 && lines[start - 1].IsIgnored)
        {
            start--;
        }

        while (end + 1 < lines.Count && lines[end + 1].IsIgnored)
        {
            end++;
        }

        CurrentIgnoredRowEnd = end;
        CurrentIgnoredRow = start;
        CurrentHunk = -1;
    }

    private void MoveToStop(DifferenceStop? stop)
    {
        if (stop is not { } target)
        {
            return;
        }

        if (target.IsIgnored)
        {
            // End before start, and both before clearing the hunk. Both changed handlers rebuild the
            // close-up, and each has to see a complete selection when it does: the end first so the
            // start's handler has a real range to build from, and the hunk cleared last so it is not
            // rebuilt from "nothing selected" on the way through.
            CurrentIgnoredRowEnd = target.EndRow;
            CurrentIgnoredRow = target.StartRow;
            CurrentHunk = -1;
        }
        else
        {
            CurrentIgnoredRow = -1;
            CurrentIgnoredRowEnd = -1;
            CurrentHunk = target.HunkIndex;
        }

        ScrollToRow = target.StartRow;

        // Same reason as MoveTo: both views are pointed at the row so switching mid-navigation lands in
        // the right place. A hunk can be found by index, but an ignored run is not a hunk in either
        // document, so it has to be found through the unified document's own row mapping.
        UnifiedScrollToRow = target.IsIgnored
            ? UnifiedRowOf(target.StartRow)
            : target.HunkIndex < UnifiedDocument.Hunks.Count
                ? UnifiedDocument.Hunks[target.HunkIndex].StartIndex
                : -1;

        Navigated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Where an aligned row sits in the unified document, or -1 when it is not shown there.</summary>
    private int UnifiedRowOf(int alignedRow)
    {
        var rows = UnifiedDocument.SourceRows;

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] == alignedRow)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether there is anything to walk, counted the same way the buttons walk it. Ignored semantic
    /// changes do not count: navigation skips them, so a document whose only differences are ignored
    /// has nothing to step through however many the tree lists.
    /// </summary>
    public bool HasDifferences
    {
        get
        {
            if (!IsJsonViewVisible)
            {
                return HasChanges;
            }

            foreach (var change in _semanticChanges)
            {
                if (!change.IsIgnored)
                {
                    return true;
                }
            }

            return false;
        }
    }

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

        // Which kind of difference Next/Previous walk changes with the view, and so does whether
        // there is one to walk.
        OnPropertyChanged(nameof(HasDifferences));
    }

    /// <summary>
    /// Notifies the computed properties that read through to the result. They have no setter for the
    /// generator to hook, so they must be raised by hand whenever the comparison is replaced.
    /// </summary>
    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasDifferences));
        OnPropertyChanged(nameof(HasCurrentHunk));
        OnPropertyChanged(nameof(CurrentIgnorePath));
        OnPropertyChanged(nameof(CanIgnoreCurrent));
        OnPropertyChanged(nameof(IgnoreCurrentTooltip));
        OnPropertyChanged(nameof(TotalLines));
        OnPropertyChanged(nameof(Hunks));
        OnPropertyChanged(nameof(Lines));
    }
}
