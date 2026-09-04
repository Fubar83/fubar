using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Fubar.Studio.UI.ViewModels;
using System.Linq;

namespace Fubar.Studio.UI.Views;

public partial class MainWindow : Window
{
    /// <summary>Set once the user has answered the unsaved-changes prompt, so the second Close - the
    /// one this handler issues itself - is not intercepted and asked about all over again.</summary>
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        // The two gestures whose target is a control rather than state: the left pane's filter box and
        // AvaloniaEdit's find bar. Both are raised as events by the view model so it never reaches into
        // the visual tree itself.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            viewModel.FilterFocusRequested += () =>
                this.FindControl<Fubar.Controls.SearchBox>("RequestFilterBox")?.Focus();

            viewModel.FindRequested += () =>
                this.GetVisualDescendants().OfType<Fubar.Controls.JsonEditor>().FirstOrDefault()?.OpenFind();

            viewModel.PaletteRequested += palette => new CommandPalette(palette).ShowDialog(this);
        };
    }

    /// <summary>
    /// Quitting used to discard an unsaved request without a word: there was no Closing handler at
    /// all, and the only notice of losing work anywhere in the app went to a status log that is
    /// collapsed by default.
    ///
    /// <para>The close is cancelled first and re-issued after the answer, because the prompt is async
    /// and <see cref="WindowClosingEventArgs"/> cannot be awaited - deciding after the window has gone
    /// is deciding too late.</para>
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainViewModel viewModel)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);

        try
        {
            if (!await viewModel.ConfirmDiscardingActiveEditAsync())
            {
                return;
            }
        }
        catch (Exception)
        {
            // Never trap the user in the application over a failed prompt. Losing an edit is bad; a
            // window that cannot be closed is worse, and the log already carries the detail.
        }

        _closeConfirmed = true;
        Close();
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
