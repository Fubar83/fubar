using System;
using System.Collections.Generic;
using Avalonia;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.UI;

/// <summary>
/// Creates and tracks the app's top-level <see cref="MainWindow"/>s. Each window runs in its own DI
/// scope so it gets an independent per-window view-model graph (its own tab set, left pane, active
/// editor) while sharing the singleton services and the theme/log. This is what makes tabs
/// tear-off-able into new windows and movable between windows (see the drag handlers in
/// MainWindow.axaml.cs). The scope is disposed when its window closes, tearing down that window's
/// workspace file-watchers with it.
/// </summary>
public sealed class WindowManager
{
    private readonly IServiceProvider _rootServices;
    private readonly List<MainWindow> _windows = [];

    public WindowManager(IServiceProvider rootServices) => _rootServices = rootServices;

    /// <summary>All currently-open windows - used by the tab-drag drop logic to find a window under
    /// the cursor to drop a tab into.</summary>
    public IReadOnlyList<MainWindow> Windows => _windows;

    /// <summary>
    /// Builds a new window with a fresh per-window view-model scope. <paramref name="isPrimary"/>
    /// marks the one window that owns session persistence (which workspaces reopen next launch) -
    /// torn-off/secondary windows are session-only so they don't clobber the saved set.
    /// </summary>
    public MainWindow CreateWindow(bool isPrimary)
    {
        var scope = _rootServices.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<MainViewModel>();
        viewModel.WorkspaceExplorer.PersistsSession = isPrimary;

        var window = new MainWindow { DataContext = viewModel };
        _windows.Add(window);
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            scope.Dispose();
        };

        // Closing the last tab closes the window - unless it's the only window left, which stays open
        // (empty) so the app doesn't vanish out from under the user.
        viewModel.WorkspaceExplorer.WorkspacesEmptied += () =>
        {
            if (_windows.Count > 1)
            {
                window.Close();
            }
        };

        return window;
    }
}
