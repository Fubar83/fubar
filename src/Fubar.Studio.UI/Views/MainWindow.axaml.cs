using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Fubar.Studio.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // No full-screen support. Avalonia's extended-client-area chrome draws a full-screen caption
    // button (the diagonal double-arrow) and this Avalonia version exposes no API to drop just that
    // button (no ExtendClientAreaChromeHints), and the button lives outside the window's own visual-
    // descendant tree so it can't be hidden by a style or tree walk either. So full-screen is removed
    // at the state level instead: if anything (that button, a hotkey) drives the window into
    // FullScreen, snap it straight back to Maximized. Minimize / maximize / restore / close are
    // untouched.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Maximized;
        }
    }

    // The title bar row is custom content (ExtendClientAreaToDecorationsHint), so none of the
    // usual OS drag-to-move/double-click-to-maximize behavior exists unless implemented here.
    // (Workspace-tab drag/reorder/tear-off is entirely owned by the reusable fc:TabStrip now.)
    private void TitleBarDragArea_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleBarDragArea_OnDoubleTapped(object? sender, TappedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
