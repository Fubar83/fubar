using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Fubar.Controls;

namespace Fubar.Controls.Gallery.Views;

/// <summary>
/// A minimal <see cref="ITabDragHost"/> for the gallery's TabStrip demo: tracks the strips of all open
/// demo windows and moves plain <see cref="DemoTab"/> items between their ObservableCollections, tears
/// off into new <see cref="GalleryTabWindow"/>s, and closes windows the drag emptied. Mirrors what an
/// app does with its real tab collections + window manager, but with no domain baggage - proof the
/// TabStrip's whole drag story works outside the main app.
/// </summary>
public sealed class GalleryTabDragHost : ITabDragHost
{
    private readonly List<TabStrip> _strips = [];

    public void Register(TabStrip strip)
    {
        if (!_strips.Contains(strip))
        {
            _strips.Add(strip);
        }
    }

    public void Unregister(TabStrip strip) => _strips.Remove(strip);

    public IEnumerable<TabStrip> PeerStrips =>
        _strips.Where(s => TopLevel.GetTopLevel(s) is Window { IsVisible: true });

    public void MoveTab(object item, TabStrip from, TabStrip to, int index)
    {
        if (item is not DemoTab tab || Tabs(from) is not { } source || Tabs(to) is not { } target || !source.Remove(tab))
        {
            return;
        }

        if (index >= 0 && index <= target.Count)
        {
            target.Insert(index, tab);
        }
        else
        {
            target.Add(tab);
        }
    }

    public void TearOff(object item, TabStrip from, PixelPoint screenPoint)
    {
        if (item is not DemoTab tab || Tabs(from) is not { } source || !source.Remove(tab))
        {
            return;
        }

        var window = new GalleryTabWindow(this, [tab])
        {
            Position = new PixelPoint(screenPoint.X - 60, screenPoint.Y - 12),
        };
        window.Show();
    }

    public void AfterDrop()
    {
        foreach (var strip in _strips.ToList())
        {
            if (Tabs(strip) is { Count: 0 } && TopLevel.GetTopLevel(strip) is GalleryTabWindow window)
            {
                window.Close();
            }
        }
    }

    private static ObservableCollection<DemoTab>? Tabs(TabStrip strip) =>
        strip.ItemsSource as ObservableCollection<DemoTab>;
}
