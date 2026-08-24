using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Fubar.Controls.Gallery.Views;

public partial class GalleryTabWindow : Window
{
    public GalleryTabWindow(GalleryTabDragHost host, IEnumerable<DemoTab> tabs)
    {
        InitializeComponent();

        var items = new ObservableCollection<DemoTab>(tabs);
        Strip.ItemsSource = items;
        Strip.DragHost = host;
        Strip.CloseCommand = new RelayCommand<DemoTab>(tab =>
        {
            if (tab is not null)
            {
                items.Remove(tab);
            }
        });
        Strip.SelectedItem = items.FirstOrDefault();

        host.Register(Strip);
        Closed += (_, _) => host.Unregister(Strip);
    }

    // Parameterless ctor for the XAML designer only.
    public GalleryTabWindow() => InitializeComponent();
}
