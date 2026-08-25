using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Fubar.Controls.Gallery.Views;

namespace Fubar.Controls.Gallery.Views.Pages;

public partial class TabStripPage : UserControl
{
    public TabStripPage()
    {
        InitializeComponent();

        // Inline strip: no DragHost, so it reorders within itself + shows the ghost, but doesn't
        // tear off (there's nowhere to go).
        var tabs = new ObservableCollection<DemoTab>(
            new[] { "Overview", "Params", "Headers", "Body", "Auth" }.Select(t => new DemoTab(t)));
        InlineStrip.ItemsSource = tabs;
        InlineStrip.CloseCommand = new RelayCommand<DemoTab>(tab =>
        {
            if (tab is not null)
            {
                tabs.Remove(tab);
            }
        });
        InlineStrip.SelectedItem = tabs.FirstOrDefault();
    }

    private void LaunchDemo_OnClick(object? sender, RoutedEventArgs e)
    {
        var host = new GalleryTabDragHost();

        var left = new GalleryTabWindow(host, new[] { "Alpha", "Beta", "Gamma" }.Select(t => new DemoTab(t)))
        {
            Title = "Window A",
            Position = new PixelPoint(200, 200),
        };
        var right = new GalleryTabWindow(host, new[] { "Delta", "Epsilon" }.Select(t => new DemoTab(t)))
        {
            Title = "Window B",
            Position = new PixelPoint(680, 200),
        };

        left.Show();
        right.Show();
    }
}
