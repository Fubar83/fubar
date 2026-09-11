using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

/// <summary>
/// Hosts the shared diff widget in a modal window. The only code-behind is closing - Avalonia's Window
/// exposes no CloseCommand to bind, and the widget brings its own scroll bridging, so this is otherwise
/// just a frame around it.
/// </summary>
public partial class DiffPreviewDialog : Window
{
    public DiffPreviewDialog() => InitializeComponent();

    public DiffPreviewDialog(DiffPreviewViewModel viewModel)
        : this() => DataContext = viewModel;

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape leaves a text field first and closes second - see Fubar.Controls.WindowEscape. F7/F8
        // are left to the KeyBindings, which route to the pane.
        if (Fubar.Controls.WindowEscape.Handle(this, e))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
