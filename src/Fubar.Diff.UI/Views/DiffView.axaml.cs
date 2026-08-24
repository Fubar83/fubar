using System.ComponentModel;
using Avalonia.Controls;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// The side-by-side diff grid.
///
/// The only code-behind is the scroll-into-view bridge for "next/previous change". Scrolling is a
/// view concern with no data representation - the view model can say WHICH row it wants shown
/// (<see cref="MainViewModel.ScrollToRow"/>), but only the control can actually scroll - so this
/// listens for that property rather than handing the view model a control reference.
/// </summary>
public partial class DiffView : UserControl
{
    private MainViewModel? _viewModel;

    public DiffView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ScrollToRow) || _viewModel is null)
        {
            return;
        }

        var index = _viewModel.ScrollToRow;
        if (index >= 0 && index < RowList.ItemCount)
        {
            RowList.ScrollIntoView(index);
            RowList.SelectedIndex = index;
        }
    }
}
