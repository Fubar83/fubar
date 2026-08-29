using Avalonia.Controls;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The Json view's close-up of the current change. Pure XAML, like <see cref="DiffDetailPane"/>: both
/// sides show a short excerpt that always fits, so there is nothing to bridge in code-behind.
/// </summary>
public partial class JsonDetailPane : UserControl
{
    public JsonDetailPane() => InitializeComponent();
}
