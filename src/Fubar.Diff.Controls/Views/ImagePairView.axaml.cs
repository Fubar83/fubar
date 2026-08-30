using Avalonia.Controls;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// Two images side by side. Pure XAML - everything it shows comes from
/// <see cref="ViewModels.ImagePairViewModel"/>, including whether the pictures decoded at all.
/// </summary>
public partial class ImagePairView : UserControl
{
    public ImagePairView() => InitializeComponent();
}
