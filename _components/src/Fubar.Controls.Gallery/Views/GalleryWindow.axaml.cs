using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Fubar.Controls.Gallery.Views.Pages;

namespace Fubar.Controls.Gallery.Views;

public partial class GalleryWindow : Window
{
    public GalleryWindow()
    {
        InitializeComponent();
        Nav.SelectedIndex = 0;
    }

    private void Nav_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            PageHost.Content = (Nav.SelectedItem as ListBoxItem)?.Content switch
            {
                "Feedback" => new FeedbackPage(),
                "Key Value Grid" => new KeyValueGridPage(),
                "Tab Strip" => new TabStripPage(),
                "Tree & Sections" => new TreePage(),
                "JSON Editor" => new JsonEditorPage(),
                _ => new PrimitivesPage(),
            };
        }
        catch (System.Exception ex)
        {
            // Surface page-construction failures instead of silently staying on the previous page.
            PageHost.Content = new TextBlock
            {
                Text = ex.ToString(),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Avalonia.Media.Brushes.OrangeRed,
                Margin = new Thickness(8),
            };
        }
    }

    private void ThemeToggle_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        var dark = ThemeToggle.IsChecked == true;
        ThemeToggle.Content = dark ? "Dark" : "Light";
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
