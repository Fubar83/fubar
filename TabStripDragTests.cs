using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fubar.Controls;

namespace Fubar.Controls.Tests;

/// <summary>
/// Drives the real pointer gesture through a laid-out <see cref="TabStrip"/> in a headless window and
/// asserts the drag actually reorders and floats - the mechanics the Gallery exercises by hand.
/// </summary>
public class TabStripDragTests
{
    private sealed record Tab(string Name)
    {
        public override string ToString() => Name;
    }

    private static (Window window, TabStrip strip, ObservableCollection<Tab> items) NewStrip(params string[] names)
    {
        var items = new ObservableCollection<Tab>(names.Select(n => new Tab(n)));
        var strip = new TabStrip { ItemsSource = items, SelectedItem = items.FirstOrDefault() };
        var window = new Window { Width = 600, Height = 120, Content = strip };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, strip, items);
    }

    private static Point Center(TabStrip strip, int index)
    {
        var container = (Control)strip.ContainerFromIndex(index)!;
        var mid = new Point(container.Bounds.Width / 2, container.Bounds.Height / 2);
        return container.TranslatePoint(mid, TopLevel.GetTopLevel(strip)!)!.Value;
    }

    [AvaloniaFact]
    public void Strip_has_no_scroll_viewer_that_would_capture_horizontal_drags()
    {
        // A ScrollViewer around the tabs adds a ScrollGestureRecognizer that captures the pointer on a
        // horizontal drag (a pan) and swallows the reorder/tear-off drag this strip owns - clicks still
        // select, but nothing drags. Regression guard: the strip template must not host one.
        var (_, strip, _) = NewStrip("A", "B", "C");
        Assert.Empty(strip.GetVisualDescendants().OfType<ScrollViewer>());
    }

    [AvaloniaFact]
    public void Dragging_over_a_strip_marks_it_as_a_drop_target_and_clears_on_drop()
    {
        var (window, strip, _) = NewStrip("A", "B", "C");

        var from = Center(strip, 0);
        var over = new Point(Center(strip, 1).X, from.Y);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        window.MouseMove(over, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("drag-over", strip.Classes); // accent outline + caret shown while landing here

        window.MouseUp(over, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("drag-over", strip.Classes); // cleared on drop
    }

    [AvaloniaFact]
    public void Drag_reorders_within_the_strip()
    {
        var (window, strip, items) = NewStrip("A", "B", "C");

        var from = Center(strip, 0);
        var pastC = new Point(Center(strip, 2).X + 5, from.Y);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(10, 0), RawInputModifiers.LeftMouseButton); // past 6px threshold
        window.MouseMove(pastC, RawInputModifiers.LeftMouseButton);                    // right half of last tab
        window.MouseUp(pastC, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "B", "C", "A" }, items.Select(t => t.Name));
    }

    [AvaloniaFact]
    public void Tab_dragged_clear_of_the_strip_floats_then_cleans_up_on_release()
    {
        var (window, strip, items) = NewStrip("A", "B", "C");

        var from = Center(strip, 0);
        var below = new Point(from.X, from.Y + 300); // well clear of the ~30px strip

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        window.MouseMove(below, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        var container = (Control)strip.ContainerFromIndex(0)!;
        Assert.Contains("tab-dragging", container.Classes);
        Assert.Contains("tab-floating", container.Classes); // floating branch ran => ghost shown

        window.MouseUp(below, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("tab-dragging", container.Classes);
        Assert.DoesNotContain("tab-floating", container.Classes);
        Assert.Equal(3, items.Count); // no host => nothing torn off, tab stays put
    }

    [AvaloniaFact]
    public void Drag_into_a_peer_strip_moves_the_tab_across_windows()
    {
        // Headless maps every window to screen origin (0,0) regardless of Window.Position, so two real
        // windows overlap and can't be told apart by screen geometry. We still exercise the real
        // cross-strip MoveTab path by having the host offer the *destination* strip first among its
        // peers: a drag that stays inside the (overlapping) tab band then resolves to that peer strip.
        var host = new FakeHost();
        var (winA, stripA, itemsA) = NewStrip("A", "B");
        var (winB, stripB, itemsB) = NewStrip("X", "Y");
        host.Attach(stripB, itemsB); // destination - offered first
        host.Attach(stripA, itemsA); // drag origin
        Dispatcher.UIThread.RunJobs();

        var from = Center(stripA, 0);
        var overStrip = new Point(Center(stripA, 1).X, from.Y); // still within the tab band

        winA.MouseDown(from, MouseButton.Left);
        winA.MouseMove(from + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        winA.MouseMove(overStrip, RawInputModifiers.LeftMouseButton);
        winA.MouseUp(overStrip, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(itemsA, t => t.Name == "A"); // left the origin strip
        Assert.Contains(itemsB, t => t.Name == "A");       // moved into the peer strip
        Assert.Equal(0, host.TearOffCount);                // a move, not a tear-off
    }

    [AvaloniaFact]
    public void Drop_clear_of_every_strip_tears_off_a_new_window()
    {
        var host = new FakeHost();
        var (winA, stripA, itemsA) = NewStrip("A", "B");
        winA.Position = new PixelPoint(0, 0);
        Dispatcher.UIThread.RunJobs();
        host.Attach(stripA, itemsA);

        var from = Center(stripA, 0);
        var below = new Point(from.X, from.Y + 300); // clear of the only strip

        winA.MouseDown(from, MouseButton.Left);
        winA.MouseMove(from + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        winA.MouseMove(below, RawInputModifiers.LeftMouseButton);
        winA.MouseUp(below, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, host.TearOffCount);
        Assert.Equal("A", (host.LastTornOff as Tab)?.Name);
        Assert.DoesNotContain(itemsA, t => t.Name == "A"); // detached from its old window
    }

    [AvaloniaFact]
    public void Small_nudge_below_the_strip_keeps_the_tab_attached()
    {
        var host = new FakeHost();
        var (winA, stripA, itemsA) = NewStrip("A", "B");
        host.Attach(stripA, itemsA);

        var from = Center(stripA, 0);
        var justBelow = new Point(from.X, stripA.Bounds.Height + 8); // within the 24px detach threshold

        winA.MouseDown(from, MouseButton.Left);
        winA.MouseMove(from + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        winA.MouseMove(justBelow, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        var container = (Control)stripA.ContainerFromIndex(0)!;
        Assert.DoesNotContain("tab-floating", container.Classes); // still docked, no ghost

        winA.MouseUp(justBelow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, host.TearOffCount);                 // not far enough to tear off
        Assert.Contains(itemsA, t => t.Name == "A");        // stayed put
    }

    [AvaloniaFact]
    public void Escape_aborts_the_drag_before_it_can_tear_off()
    {
        var host = new FakeHost();
        var (winA, stripA, itemsA) = NewStrip("A", "B");
        host.Attach(stripA, itemsA);

        var from = Center(stripA, 0);
        var below = new Point(from.X, from.Y + 300); // clear of the strip => floating

        winA.MouseDown(from, MouseButton.Left);
        winA.MouseMove(from + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        winA.MouseMove(below, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        var container = (Control)stripA.ContainerFromIndex(0)!;
        Assert.Contains("tab-floating", container.Classes); // floating before we cancel

        winA.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("tab-floating", container.Classes); // ghost + float cleared

        winA.MouseUp(below, MouseButton.Left); // the release that would have torn off
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, host.TearOffCount);          // cancelled, so no new window
        Assert.Contains(itemsA, t => t.Name == "A"); // tab stayed in its strip
    }

    // Minimal ITabDragHost over the test's plain collections - mirrors GalleryTabDragHost.
    private sealed class FakeHost : ITabDragHost
    {
        private readonly List<(TabStrip strip, ObservableCollection<Tab> items)> _strips = [];

        public int TearOffCount { get; private set; }
        public object? LastTornOff { get; private set; }

        public void Attach(TabStrip strip, ObservableCollection<Tab> items)
        {
            strip.DragHost = this;
            _strips.Add((strip, items));
        }

        public System.Collections.Generic.IEnumerable<TabStrip> PeerStrips => _strips.Select(s => s.strip);

        public void MoveTab(object item, TabStrip from, TabStrip to, int index)
        {
            var src = Items(from);
            var dst = Items(to);
            if (item is not Tab tab || src is null || dst is null || !src.Remove(tab))
            {
                return;
            }

            if (index >= 0 && index <= dst.Count)
            {
                dst.Insert(index, tab);
            }
            else
            {
                dst.Add(tab);
            }
        }

        public void TearOff(object item, TabStrip from, PixelPoint screenPoint)
        {
            TearOffCount++;
            LastTornOff = item;
            if (item is Tab tab)
            {
                Items(from)?.Remove(tab);
            }
        }

        public void AfterDrop() { }

        private ObservableCollection<Tab>? Items(TabStrip strip) =>
            _strips.FirstOrDefault(s => ReferenceEquals(s.strip, strip)).items;
    }
}
