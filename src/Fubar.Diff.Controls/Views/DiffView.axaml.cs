using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Fubar.Diff.Controls.Rendering;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The side-by-side diff: two editor panes plus the diff map between them.
///
/// All the code-behind here exists because scrolling has no data representation. The view model can
/// say WHICH row it wants shown (<see cref="DiffPaneViewModel.ScrollToRow"/>) and needs to know which rows
/// are visible (<see cref="DiffPaneViewModel.ViewportStart"/>), but only the controls can scroll or
/// measure themselves - so this bridges the two rather than handing the view model a control.
/// </summary>
public partial class DiffView : UserControl
{
    private DiffPaneViewModel? _viewModel;

    /// <summary>
    /// Guards the two scroll handlers against each other. Setting one pane's offset raises its own
    /// ScrollOffsetChanged, which would set the other back, forever.
    /// </summary>
    private bool _syncingScroll;

    /// <summary>
    /// The two rows the panes are being held level at, or -1 for ordinary lockstep.
    ///
    /// <para>Set only for a MOVE, whose two halves are at different rows by definition. Lockstep is
    /// right for every other difference precisely because the panes are row-aligned - row N is the same
    /// row on both sides - but a moved block breaks that on purpose: the block is at one row on the left
    /// and another on the right, and holding the panes level would put at most one of its two ends on
    /// screen. Offsetting them by the move's own distance puts both on screen at once, which is the only
    /// way to actually read a move.</para>
    ///
    /// <para>Stored as ROWS, not as a pixel offset. Folding and word wrap both change what a row is worth
    /// in pixels, and a cached offset would quietly drift the moment a region collapsed above either end.
    /// Asking the text view where the rows are, every time, cannot drift.</para>
    /// </summary>
    private int _syncLeftRow = -1;

    private int _syncRightRow = -1;

    public DiffView()
    {
        InitializeComponent();

        LeftPane.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(from: LeftPane, to: RightPane);
        RightPane.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(from: RightPane, to: LeftPane);

        // The authoritative "the viewport is now measurable" signal. Posting to the dispatcher after a
        // document change is not enough - that runs before layout, so VisualLines is still empty and
        // the viewport reads as zero, which silently collapses the diff map's scale.
        LeftPane.TextView.VisualLinesChanged += (_, _) => ReportViewport();

        Map.JumpRequested += (_, row) => _viewModel?.JumpToRow(row);

        // Clicking a difference in either pane makes it the current one. Released rather than pressed,
        // so AvaloniaEdit has already moved its caret and there is a line to read; and released rather
        // than a caret-changed subscription, because the caret also moves when WE scroll to a
        // difference, which would feed straight back into selecting it again.
        LeftPane.AddHandler(PointerReleasedEvent, OnPaneClicked, RoutingStrategies.Bubble, handledEventsToo: true);
        RightPane.AddHandler(PointerReleasedEvent, OnPaneClicked, RoutingStrategies.Bubble, handledEventsToo: true);

        // The panes own their documents, their filler anchors and their carets; the view model owns
        // what a comparison MEANS. Edits cross that line here - the side that changed goes up, and the
        // text comes back down only when someone asks for it, because reading a document costs a pass
        // over it and this fires on every keystroke.
        LeftPane.Edited += (_, _) => _viewModel?.ReportEdit(DiffSide.Left);
        RightPane.Edited += (_, _) => _viewModel?.ReportEdit(DiffSide.Right);

        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Row heights the splitter starts from, restored when the detail pane is shown again.</summary>
    private GridLength _detailHeight = new(260);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as DiffPaneViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // The map colours each tick by change kind, and the rows are a plain list rather than a
            // styled property, so it is handed over directly instead of bound.
            Map.DiffLines = _viewModel.Lines;

            // Same reasoning: the pane knows how to take its document back apart, and the view model
            // has no business holding a control to ask it.
            _viewModel.FileLinesReader = side =>
                side == DiffSide.Left ? LeftPane.ReadFileLines() : RightPane.ReadFileLines();

            _viewModel.RowReplacer = (side, first, last, lines) =>
                (side == DiffSide.Left ? LeftPane : RightPane).ReplaceRows(first, last, lines);

            _viewModel.CaretLineReader = side =>
                (side == DiffSide.Left ? LeftPane : RightPane).CaretSourceLine();

            ApplyCurrentHunk();
            ApplyDetailVisibility();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(DiffPaneViewModel.ScrollToRow):
                ScrollTo(_viewModel.ScrollToRow);
                break;

            case nameof(DiffPaneViewModel.Lines):
                Map.DiffLines = _viewModel.Lines;
                break;

            case nameof(DiffPaneViewModel.CurrentHunk):
            case nameof(DiffPaneViewModel.CurrentIgnoredRow):
            case nameof(DiffPaneViewModel.CurrentIgnoredRowEnd):
                ApplyCurrentHunk();
                break;

            case nameof(DiffPaneViewModel.IsDetailVisible):
                ApplyDetailVisibility();
                break;

            // A new document invalidates the reported viewport, but recomputing it here would read a
            // stale layout. TextView.VisualLinesChanged fires once the new document has been laid
            // out, and that is what updates it.
        }
    }

    /// <summary>
    /// Copies one pane's scroll offset to the other, and reports the visible range up to the view
    /// model for the diff map.
    /// </summary>
    private void SyncScroll(DiffEditorPane from, DiffEditorPane to)
    {
        if (_syncingScroll)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            // BOTH axes. Horizontal used to be left independent, on the argument that yanking one
            // pane sideways because the other has a long line is disorienting - but that reasoning
            // only holds for a pane you are not reading. The rows are aligned, so row N is the same
            // change on both sides, and scrolling right to reach the end of a long line puts the
            // counterpart line off screen exactly when it is the thing being compared. Two columns
            // that have to be dragged sideways separately to read one difference is the worse of the
            // two problems.
            //
            // The two axes are WRITTEN THROUGH DIFFERENT OBJECTS, which is not a tidiness problem to
            // fix: vertical goes through the TextEditor, which routes it to the text view internally,
            // and horizontal has to go through the text view itself because the editor's counterpart
            // silently does nothing. See EditorScroll.ScrollHorizontallyTo.
            var horizontal = from.TextView.HorizontalOffset;

            // Vertical carries the move offset: zero for every ordinary difference, so this stays the
            // plain offset copy it has always been, and the two ends' distance apart while a moved block
            // is the current difference.
            var offset = SyncOffset();
            var vertical = from.TextView.VerticalOffset
                + (ReferenceEquals(from, LeftPane) ? offset : -offset);

            if (Math.Abs(to.TextView.VerticalOffset - vertical) > 0.5)
            {
                EditorScroll.ScrollVerticallyTo(to.TextView, vertical);
            }

            if (Math.Abs(to.TextView.HorizontalOffset - horizontal) > 0.5)
            {
                EditorScroll.ScrollHorizontallyTo(to.TextView, horizontal);
            }
        }
        finally
        {
            _syncingScroll = false;
        }

        ReportViewport();
    }

    /// <summary>
    /// Pushes the current hunk's row range into both panes so it is drawn as the selected block.
    ///
    /// Rows rather than a hunk index, because the renderers know nothing about hunks - they paint
    /// document lines, and the range is the only part of a hunk that means anything to them.
    /// </summary>
    private void ApplyCurrentHunk()
    {
        if (_viewModel is null)
        {
            return;
        }

        // Asked per SIDE, and the view model works out what that means. Both panes used to be handed the
        // same row range, which is right for every difference except a MOVE - whose two halves are on
        // different rows by definition, so one pane got the block and the other got whatever unrelated
        // context happened to sit at those rows. It also covers an ignored run, which is a difference you
        // can be on without it being a hunk at all.
        var (leftStart, leftEnd) = _viewModel.CurrentRangeFor(DiffSide.Left);
        var (rightStart, rightEnd) = _viewModel.CurrentRangeFor(DiffSide.Right);

        LeftPane.SetCurrentHunk(leftStart, leftEnd);
        RightPane.SetCurrentHunk(rightStart, rightEnd);

        // Hold the panes level at the move's two ends rather than at the same row. Cleared for anything
        // else, so the offset lasts exactly as long as the move is the difference being read.
        var moved = leftStart >= 0 && rightStart >= 0 && leftStart != rightStart;
        var wasMoved = _syncLeftRow >= 0;

        _syncLeftRow = moved ? leftStart : -1;
        _syncRightRow = moved ? rightStart : -1;

        // Take up the new offset now rather than waiting for the next scroll. Selecting a move by
        // CLICKING it requests no scroll of its own - a click should not move the page - so without this
        // the far end stayed off screen until something else happened to scroll, and the panes would
        // then jump apart for no reason the reader could connect to what they did.
        //
        // Only when the offset actually changes, so an ordinary re-selection leaves the view alone.
        if (moved || wasMoved)
        {
            ScrollTo(leftStart);
        }
    }

    /// <summary>
    /// Collapses or restores the detail pane. The splitter and the pane both need their ROW heights
    /// zeroed, not just IsVisible: a hidden child still leaves its row occupying 260px, which would
    /// show as a blank band under the panes.
    /// </summary>
    private void ApplyDetailVisibility()
    {
        var visible = _viewModel?.IsDetailVisible ?? true;

        var splitterRow = Root.RowDefinitions[1];
        var detailRow = Root.RowDefinitions[2];

        if (visible)
        {
            splitterRow.Height = GridLength.Auto;
            detailRow.Height = _detailHeight;
        }
        else
        {
            // Remember whatever the user dragged it to, so re-showing does not reset their layout.
            if (detailRow.Height.Value > 0)
            {
                _detailHeight = detailRow.Height;
            }

            splitterRow.Height = new GridLength(0);
            detailRow.Height = new GridLength(0);
        }

        DetailSplitter.IsVisible = visible;
        Detail.IsVisible = visible;
    }

    /// <summary>Tells the view model which rows are on screen, so the map can draw its viewport box.</summary>
    private void ReportViewport()
    {
        if (_viewModel is null)
        {
            return;
        }

        var textView = LeftPane.TextView;
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0)
        {
            return;
        }

        _viewModel.ViewportStart = textView.VisualLines[0].FirstDocumentLine.LineNumber - 1;

        // How many lines FIT, not how many are currently drawn. For a document shorter than the pane
        // those differ, and using the drawn count would make the map think the file exactly fills the
        // viewport - collapsing its scale and pushing every tick far below the line it points at.
        var lineHeight = textView.DefaultLineHeight;
        _viewModel.ViewportLength = lineHeight > 0
            ? Math.Max((int)Math.Ceiling(textView.Bounds.Height / lineHeight), 1)
            : Math.Max(textView.VisualLines.Count, 1);
    }

    /// <summary>Centres a row in the viewport in both panes.</summary>
    /// <summary>
    /// Scrolls each pane sideways so the row's changed characters are visible.
    ///
    /// A row with no spans - a whole inserted or deleted line - reports column 0, which
    /// <see cref="EditorScroll.RevealColumns"/> reads as "go home": the change starts at the beginning
    /// of the line, and staying parked to the right would hide it.
    /// </summary>
    private void RevealChangedColumns(int rowIndex)
    {
        if (_viewModel?.Lines is not { } lines || rowIndex >= lines.Count)
        {
            return;
        }

        var row = lines[rowIndex];

        Reveal(LeftPane, row.LeftSpans);
        Reveal(RightPane, row.RightSpans);

        void Reveal(DiffEditorPane pane, IReadOnlyList<CharSpan> spans)
        {
            // 1-based columns from 0-based offsets.
            var start = spans.Count == 0 ? 0 : spans.Min(s => s.Start) + 1;
            var end = spans.Count == 0 ? 0 : spans.Max(s => s.End) + 1;

            EditorScroll.RevealColumns(pane.TextEditor, pane.TextView, rowIndex + 1, start, end);
        }
    }

    /// <summary>
    /// Selects the difference under the pointer, if there is one.
    ///
    /// The row is the caret's line: both panes are the aligned document, so editor line N is
    /// <c>DiffResult.Lines[N-1]</c> on either side - the filler discipline is what makes reading it off
    /// one pane and applying it to the comparison correct without any mapping.
    /// </summary>
    private void OnPaneClicked(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not DiffEditorPane pane || _viewModel is null)
        {
            return;
        }

        var line = pane.TextEditor.TextArea.Caret.Line;
        if (line > 0)
        {
            _viewModel.SelectDifferenceAtRow(line - 1);
        }
    }

    /// <summary>
    /// How far the right pane sits below the left, in pixels. Zero unless a moved block is selected.
    ///
    /// Asked of the text views rather than computed as rows x line height: lines are only uniformly tall
    /// when nothing is folded and nothing wraps, and a collapsed region above either end would otherwise
    /// throw the two panes out by exactly the rows it hid.
    /// </summary>
    private double SyncOffset()
    {
        if (_syncLeftRow < 0 || _syncRightRow < 0)
        {
            return 0;
        }

        var leftDocument = LeftPane.TextEditor.Document;
        var rightDocument = RightPane.TextEditor.Document;

        if (leftDocument is null || rightDocument is null
            || _syncLeftRow >= leftDocument.LineCount || _syncRightRow >= rightDocument.LineCount)
        {
            return 0;
        }

        return RightPane.TextView.GetVisualTopByDocumentLine(_syncRightRow + 1)
            - LeftPane.TextView.GetVisualTopByDocumentLine(_syncLeftRow + 1);
    }

    private void ScrollTo(int rowIndex)
    {
        if (rowIndex < 0)
        {
            return;
        }

        var document = LeftPane.TextEditor.Document;
        if (document is null || rowIndex >= document.LineCount)
        {
            return;
        }

        // Both panes are told explicitly rather than relying on the sync handler, because a
        // programmatic scroll of one may not move far enough to trip it.
        _syncingScroll = true;
        try
        {
            // Each pane on its OWN end when a move is selected, so both highlighted ends are on screen
            // at once; on the same row for everything else, which is every other difference.
            var leftRow = _syncLeftRow >= 0 ? _syncLeftRow : rowIndex;
            var rightRow = _syncRightRow >= 0 ? _syncRightRow : rowIndex;

            EditorScroll.CenterOnLine(LeftPane.TextEditor, LeftPane.TextView, leftRow + 1);
            EditorScroll.CenterOnLine(RightPane.TextEditor, RightPane.TextView, rightRow + 1);

            // Sideways too, or navigating to a change beyond the right edge of a long line lands on a
            // row that looks unchanged. Each side is given ITS OWN columns: on a modified row the two
            // sides' changed characters are rarely at the same offsets, and using one side's for both
            // would leave the other pointing at the wrong part of its line.
            RevealChangedColumns(rowIndex);
        }
        finally
        {
            _syncingScroll = false;
        }

        ReportViewport();
    }

    /// <summary>
    /// Opens the find bar. Targets whichever pane has focus, falling back to the left - Ctrl+F in a
    /// two-pane view means "search where I am looking".
    /// </summary>
    public void OpenSearch()
    {
        if (RightPane.TextEditor.TextArea.IsFocused || RightPane.TextEditor.IsFocused)
        {
            RightPane.OpenSearch();
        }
        else
        {
            LeftPane.OpenSearch();
        }
    }

    /// <summary>Re-resolves palette colours in both panes after a theme switch.</summary>
    public void OnThemeChanged()
    {
        LeftPane.OnThemeChanged();
        RightPane.OnThemeChanged();
        Map.InvalidateVisual();
    }
}
