using Avalonia.Controls;

namespace Fubar.Controls.Gallery.Views.Pages;

public partial class FeedbackPage : UserControl
{
    public FeedbackPage()
    {
        InitializeComponent();
        Segments.ItemsSource = new[] { "Pretty", "Raw", "Preview" };
        Segments.SelectedIndex = 0;
    }
}
