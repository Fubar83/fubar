using System.ComponentModel;
using Avalonia.Controls;
using Fubar.Diff.Controls.ViewModels;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The Json view's close-up of the current change.
///
/// <para>The code-behind exists for one thing: a side with nothing in it gets no height. An inserted
/// value exists only on the right and a deleted one only on the left, so for a large share of the
/// changes anyone navigates to, an even split spends half the pane on an empty box - and it does that
/// beside the half holding the thing the reader is actually trying to read. A minified document is the
/// worst case, because there the excerpt is the whole line and the side with content needs every pixel
/// it can get.</para>
///
/// <para>Done by zeroing the <c>RowDefinition</c> rather than by hiding the child: hiding alone leaves
/// the empty band exactly where it was, which is the gotcha DiffView's own detail pane already
/// documents.</para>
/// </summary>
public partial class JsonDetailPane : UserControl
{
    private static readonly GridLength Half = new(1, GridUnitType.Star);
    private static readonly GridLength Hairline = new(1, GridUnitType.Pixel);
    private static readonly GridLength Collapsed = new(0, GridUnitType.Pixel);

    private DiffPaneViewModel? _viewModel;

    public JsonDetailPane()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rebind();
    }

    private void Rebind()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as DiffPaneViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplySideHeights();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiffPaneViewModel.HasDetailLeft) or nameof(DiffPaneViewModel.HasDetailRight))
        {
            ApplySideHeights();
        }
    }

    private void ApplySideHeights()
    {
        // Both empty is left alone rather than collapsed to nothing: the pane is between comparisons or
        // on a change with no excerpt either way, and a band that vanishes and returns as the user steps
        // through differences is worse than one that stays put.
        var left = _viewModel?.HasDetailLeft ?? true;
        var right = _viewModel?.HasDetailRight ?? true;

        if (!left && !right)
        {
            left = right = true;
        }

        Sides.RowDefinitions[0].Height = left ? Half : Collapsed;
        Sides.RowDefinitions[2].Height = right ? Half : Collapsed;

        // The divider only means something between two visible sides.
        Sides.RowDefinitions[1].Height = left && right ? Hairline : Collapsed;

        LeftSide.IsVisible = left;
        SidesDivider.IsVisible = left && right;
        RightSide.IsVisible = right;
    }
}
