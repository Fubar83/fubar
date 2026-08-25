using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.VisualTree;
using Fubar.Controls;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;

namespace Fubar.Studio.UI.Tabs;

/// <summary>
/// App-side bridge that lets the reusable <see cref="TabStrip"/> move workspace tabs between windows and
/// tear them off into new ones. Maps each strip to its window's <see cref="WorkspaceExplorerViewModel"/>
/// (via the strip's <c>MainViewModel</c> DataContext) and drives the moves through the explorer's
/// existing <see cref="WorkspaceExplorerViewModel.DetachRoot"/>/<see cref="WorkspaceExplorerViewModel.AttachRoot"/>
/// (which reuse the same <see cref="WorkspaceRootViewModel"/> instance + its live file-watcher, so
/// nothing reloads). New/empty windows go through <see cref="WindowManager"/>.
/// </summary>
public sealed class WorkspaceTabDragHost : ITabDragHost
{
    private readonly WindowManager _windows;

    public WorkspaceTabDragHost(WindowManager windows) => _windows = windows;

    public IEnumerable<TabStrip> PeerStrips =>
        _windows.Windows
            .Select(w => w.GetVisualDescendants().OfType<TabStrip>().FirstOrDefault())
            .OfType<TabStrip>();

    public void MoveTab(object item, TabStrip from, TabStrip to, int index)
    {
        if (item is not WorkspaceRootViewModel root ||
            Explorer(from) is not { } fromExplorer ||
            Explorer(to) is not { } toExplorer)
        {
            return;
        }

        var detached = fromExplorer.DetachRoot(root.FullPath);
        if (detached is not null)
        {
            toExplorer.AttachRoot(detached, index);
        }
    }

    public void TearOff(object item, TabStrip from, PixelPoint screenPoint)
    {
        if (item is not WorkspaceRootViewModel root || Explorer(from) is not { } fromExplorer)
        {
            return;
        }

        var detached = fromExplorer.DetachRoot(root.FullPath);
        if (detached is null)
        {
            return;
        }

        var window = _windows.CreateWindow(isPrimary: false);
        window.Position = new PixelPoint(screenPoint.X - 60, screenPoint.Y - 12);
        window.Show();
        ((MainViewModel)window.DataContext!).WorkspaceExplorer.AttachRoot(detached, -1);
        window.Activate();
    }

    // Close every non-primary window the drag left empty (its last tab moved elsewhere), like a browser
    // closing a window when its final tab is dragged out. Snapshot first (Close mutates the list).
    public void AfterDrop()
    {
        foreach (var window in _windows.Windows.ToList())
        {
            if (window.DataContext is MainViewModel { WorkspaceExplorer: { PersistsSession: false, Roots.Count: 0 } })
            {
                window.Close();
            }
        }
    }

    private static WorkspaceExplorerViewModel? Explorer(TabStrip strip) =>
        (strip.DataContext as MainViewModel)?.WorkspaceExplorer;
}
