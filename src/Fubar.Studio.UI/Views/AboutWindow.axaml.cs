using Avalonia.Controls;
using Avalonia.Input;
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

    /// <summary>
    /// Escape leaves a text field first and closes the window second - see
    /// <see cref="Fubar.Controls.WindowEscape"/>.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Fubar.Controls.WindowEscape.Handle(this, e))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
