using System;
using System.ComponentModel;
using Avalonia.Controls;
using Fubar.Diff.Controls.ViewModels;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// Tree-plus-both-documents view for a semantic JSON comparison, plus its own close-up of the current
/// change - the Json-mode counterpart to <see cref="DiffView"/>'s detail pane, toggled by the same
/// "Diff pane" checkbox. Otherwise pure XAML: unlike <see cref="DiffView"/> there is no cross-document
/// scroll sync to bridge, since each <see cref="RawJsonPane"/> scrolls itself to its own highlighted
/// span independently.
/// </summary>
public partial class JsonView : UserControl
{
    private DiffPaneViewModel? _viewModel;

    /// <summary>Row height the splitter starts from, restored when the detail pane is shown again.</summary>
    private GridLength _detailHeight = new(260);

    public JsonView()
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
            ApplyDetailVisibility();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffPaneViewModel.IsDetailVisible))
        {
            ApplyDetailVisibility();
        }
    }

    /// <summary>
    /// Collapses or restores the detail pane. The splitter and the pane both need their ROW heights
    /// zeroed, not just IsVisible - see <see cref="DiffView"/>'s identical method, which this mirrors.
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
}
