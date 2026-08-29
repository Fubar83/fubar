using System;
using System.ComponentModel;
using Avalonia.Controls;
using Fubar.Diff.Controls.Rendering;
using Fubar.Diff.Controls.ViewModels;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The three-way merge: the ancestor between the two edits, all three locked in step.
///
/// The code-behind exists for the same reason <c>DiffView</c>'s does - scrolling has no data
/// representation. The view model can say which row it wants shown and which region is current, but
/// only the controls can scroll or measure themselves.
///
/// What is different here is only the arity. Three panes means three scroll handlers rather than two,
/// and the same re-entry guard has to cover all of them: setting one pane's offset raises its own
/// ScrollOffsetChanged, which would set the others back, forever.
/// </summary>
public partial class ThreeWayView : UserControl
{
    private ThreeWayPaneViewModel? _viewModel;

    private bool _syncingScroll;

    public ThreeWayView()
    {
        InitializeComponent();

        LeftPane.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(LeftPane);
        BasePane.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(BasePane);
        RightPane.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(RightPane);

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as ThreeWayPaneViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyCurrentRegion();
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
            case nameof(ThreeWayPaneViewModel.ScrollToRow):
                ScrollTo(_viewModel.ScrollToRow);
                break;

            case nameof(ThreeWayPaneViewModel.CurrentRegion):
                ApplyCurrentRegion();
                break;
        }
    }

    /// <summary>Copies one pane's vertical offset to the other two.</summary>
    private void SyncScroll(DiffEditorPane from)
    {
        if (_syncingScroll)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            // Vertical only, as in the two-way view: lines differ in length between the three
            // documents, and dragging every pane sideways because one has a long line is more
            // disorienting than helpful.
            var offset = from.TextView.VerticalOffset;

            foreach (var pane in new[] { LeftPane, BasePane, RightPane })
            {
                if (!ReferenceEquals(pane, from) && Math.Abs(pane.TextView.VerticalOffset - offset) > 0.5)
                {
                    pane.TextEditor.ScrollToVerticalOffset(offset);
                }
            }
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    /// <summary>
    /// Pushes the selected region's row range into all three panes, so it is drawn as the selected
    /// block in each. Rows rather than a region index - the renderers paint document lines and know
    /// nothing about merges.
    /// </summary>
    private void ApplyCurrentRegion()
    {
        if (_viewModel is null)
        {
            return;
        }

        var (start, end) = _viewModel.SelectedRegion is { } region
            ? (region.StartIndex, region.EndIndex)
            : (-1, -1);

        LeftPane.SetCurrentHunk(start, end);
        BasePane.SetCurrentHunk(start, end);
        RightPane.SetCurrentHunk(start, end);
    }

    /// <summary>Centres a row in all three panes.</summary>
    private void ScrollTo(int rowIndex)
    {
        if (rowIndex < 0)
        {
            return;
        }

        var document = BasePane.TextEditor.Document;
        if (document is null || rowIndex >= document.LineCount)
        {
            return;
        }

        // Every pane is told explicitly rather than relying on the sync handler, because a
        // programmatic scroll of one may not move far enough to trip it.
        _syncingScroll = true;
        try
        {
            EditorScroll.CenterOnLine(LeftPane.TextEditor, LeftPane.TextView, rowIndex + 1);
            EditorScroll.CenterOnLine(BasePane.TextEditor, BasePane.TextView, rowIndex + 1);
            EditorScroll.CenterOnLine(RightPane.TextEditor, RightPane.TextView, rowIndex + 1);
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    /// <summary>Opens the find bar in whichever pane has focus, falling back to the ancestor.</summary>
    public void OpenSearch()
    {
        if (LeftPane.TextEditor.TextArea.IsFocused || LeftPane.TextEditor.IsFocused)
        {
            LeftPane.OpenSearch();
        }
        else if (RightPane.TextEditor.TextArea.IsFocused || RightPane.TextEditor.IsFocused)
        {
            RightPane.OpenSearch();
        }
        else
        {
            BasePane.OpenSearch();
        }
    }

    /// <summary>Re-resolves palette colours in all three panes after a theme switch.</summary>
    public void OnThemeChanged()
    {
        LeftPane.OnThemeChanged();
        BasePane.OnThemeChanged();
        RightPane.OnThemeChanged();
    }
}
