using Avalonia.Controls;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// Tree-plus-both-documents view for a semantic JSON comparison. Pure XAML: unlike <see cref="DiffView"/>
/// there is no cross-document scroll sync to bridge - each <see cref="RawJsonPane"/> scrolls itself to
/// its own highlighted span independently, which is the whole point of not aligning the two documents
/// at all.
/// </summary>
public partial class JsonView : UserControl
{
    public JsonView() => InitializeComponent();
}
