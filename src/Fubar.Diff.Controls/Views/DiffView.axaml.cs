using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

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
            // Vertical only. Horizontal is deliberately left independent: lines differ in length
            // between the two sides, and yanking one pane sideways because the other has a long line
            // is more disorienting than helpful.
            //
            // The offset is read from the TextView but written through the TextEditor - TextView's
            // ScrollOffset is read-only, and the editor owns the scroll viewer that actually moves.
            var offset = from.TextView.VerticalOffset;

            if (Math.Abs(to.TextView.VerticalOffset - offset) > 0.5)
            {
                to.TextEditor.ScrollToVerticalOffset(offset);
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

        var (start, end) = _viewModel.HasCurrentHunk
            ? (_viewModel.Hunks[_viewModel.CurrentHunk].StartIndex, _viewModel.Hunks[_viewModel.CurrentHunk].EndIndex)
            : (-1, -1);

        LeftPane.SetCurrentHunk(start, end);
        RightPane.SetCurrentHunk(start, end);
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
            EditorScroll.CenterOnLine(LeftPane.TextEditor, LeftPane.TextView, rowIndex + 1);
            EditorScroll.CenterOnLine(RightPane.TextEditor, RightPane.TextView, rowIndex + 1);
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
