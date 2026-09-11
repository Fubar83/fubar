using Avalonia.Controls;
using Avalonia.Input;
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
