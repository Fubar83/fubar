using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// The structure panel - see <see cref="ViewModels.CodeStructureViewModel"/>.
///
/// No code-behind of its own: selection drives navigation through a two-way binding, and there is
/// nothing here that needs to measure or scroll itself.
/// </summary>
public partial class CodeStructurePanel : UserControl
{
    public CodeStructurePanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
