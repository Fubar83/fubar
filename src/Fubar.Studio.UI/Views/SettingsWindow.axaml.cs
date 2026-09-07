using Avalonia.Controls;
using Avalonia.Interactivity;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.Saved += Close;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close();
}
