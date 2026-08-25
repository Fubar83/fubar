using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fubar.Studio.UI.Views;

/// <summary>A small modal prompt for pasting a curl command. Returns the pasted text via
/// <c>ShowDialog&lt;string?&gt;</c> (null on cancel / empty).</summary>
public partial class CurlImportDialog : Window
{
    public CurlImportDialog()
    {
        InitializeComponent();
    }

    private void Import_Click(object? sender, RoutedEventArgs e)
    {
        var text = CurlInput.Text;
        Close(string.IsNullOrWhiteSpace(text) ? null : text);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
