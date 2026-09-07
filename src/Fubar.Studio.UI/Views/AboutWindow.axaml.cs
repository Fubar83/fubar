using Avalonia.Controls;
using Avalonia.Interactivity;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    public AboutWindow(AboutViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
