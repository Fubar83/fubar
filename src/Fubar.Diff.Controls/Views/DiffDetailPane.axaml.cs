using Avalonia.Controls;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The close-up of the current difference. Pure XAML: both sides show a short excerpt that always
/// fits, so unlike the main view there is no scroll to keep in step and nothing to bridge.
/// </summary>
public partial class DiffDetailPane : UserControl
{
    public DiffDetailPane() => InitializeComponent();
}
