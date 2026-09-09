using Avalonia.Controls;
using Avalonia.Input;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

/// <summary>
/// The environment-comparison window: the plan on the left, the selected request's comparison on the
/// right.
///
/// <para>The only code-behind is selecting a row. The list is an ItemsControl rather than a ListBox
/// because each row draws two status lines and its own verdict pill, and a ListBox's own selection
/// visuals would have to be undone before any of that could be styled.</para>
/// </summary>
public partial class EnvironmentComparisonWindow : Window
{
    public EnvironmentComparisonWindow() => InitializeComponent();

    public EnvironmentComparisonWindow(EnvironmentComparisonViewModel viewModel)
        : this() => DataContext = viewModel;

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ComparisonRowViewModel row } &&
            DataContext is EnvironmentComparisonViewModel viewModel)
        {
            viewModel.SelectedRow = row;
        }
    }
}
