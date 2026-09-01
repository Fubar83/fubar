using System;
using System.ComponentModel;
using Avalonia.Controls;
using Fubar.Diff.Controls.Rendering;
using Fubar.Diff.Controls.ViewModels;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The unified view: one editor showing the whole comparison as a patch.
///
/// Far less code-behind than <see cref="DiffView"/> needs, and for a good reason - with one editor
/// there is no scroll to keep in step and no second pane to mirror. What remains is the same pair of
/// jobs every view here has: scroll where the view model asks, and mark the current hunk. Both are
/// expressed in the UNIFIED document's own row indices, which are not the comparison's.
/// </summary>
public partial class UnifiedView : UserControl
{
    private DiffPaneViewModel? _viewModel;

    public UnifiedView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

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
            ApplyCurrentHunk();
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
            case nameof(DiffPaneViewModel.UnifiedScrollToRow):
                ScrollTo(_viewModel.UnifiedScrollToRow);
                break;

            case nameof(DiffPaneViewModel.CurrentHunk):
                ApplyCurrentHunk();
                break;
        }
    }

    /// <summary>
    /// Marks the current hunk, translated into unified rows. The hunk INDEX is the same in both
    /// coordinate systems - <c>UnifiedText</c> emits one range per hunk, in order - so only the row
    /// numbers differ.
    /// </summary>
    private void ApplyCurrentHunk()
    {
        if (_viewModel is null)
        {
            return;
        }

        var hunks = _viewModel.UnifiedDocument.Hunks;
        var index = _viewModel.CurrentHunk;

        var (start, end) = index >= 0 && index < hunks.Count
            ? (hunks[index].StartIndex, hunks[index].EndIndex)
            : (-1, -1);

        Pane.SetCurrentHunk(start, end);
    }

    private void ScrollTo(int rowIndex)
    {
        if (rowIndex < 0)
        {
            return;
        }

        var document = Pane.TextEditor.Document;
        if (document is null || rowIndex >= document.LineCount)
        {
            return;
        }

        EditorScroll.CenterOnLine(Pane.TextEditor, Pane.TextView, rowIndex + 1);
    }

    /// <summary>Opens the find bar.</summary>
    public void OpenSearch() => Pane.OpenSearch();

    /// <summary>Re-resolves palette colours after a theme switch.</summary>
    public void OnThemeChanged() => Pane.OnThemeChanged();
}
