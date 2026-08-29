using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// The detailed comparison-settings dialog. Pure XAML apart from closing itself - every option is a
/// direct two-way binding onto the <c>ComparisonViewModel</c> passed in as its DataContext, and
/// changing one re-runs the comparison exactly the way the toolbar's controls already did (see
/// <c>ComparisonViewModel.OptionChanged</c>) - this window is just a roomier place to reach them from.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
