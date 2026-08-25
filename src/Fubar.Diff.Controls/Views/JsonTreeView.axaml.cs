using Avalonia.Controls;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The semantic change tree. Pure XAML - no code-behind beyond initialisation, because unlike the
/// editors it needs no scroll bridging.
/// </summary>
public partial class JsonTreeView : UserControl
{
    public JsonTreeView() => InitializeComponent();
}
