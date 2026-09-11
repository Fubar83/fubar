using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

/// <summary>
/// The command palette window.
///
/// <para>Keyboard handling lives here rather than in KeyBindings because the arrow keys have to be
/// intercepted while focus is in the TEXT BOX - a KeyBinding on the window would never see them, and
/// moving focus to the list to navigate would stop the user typing.</para>
/// </summary>
public partial class CommandPalette : Window
{
    public CommandPalette()
    {
        InitializeComponent();
    }

    public CommandPalette(CommandPaletteViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.Accepted += Close;

        // Focus after the window is up: focusing a control that has not been laid out yet does
        // nothing, silently, and the palette would open with no caret in it.
        Opened += (_, _) => QueryBox.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel viewModel)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                viewModel.MoveDownCommand.Execute(null);
                ScrollToSelection();
                e.Handled = true;
                return;

            case Key.Up:
                viewModel.MoveUpCommand.Execute(null);
                ScrollToSelection();
                e.Handled = true;
                return;

            case Key.Enter:
                viewModel.AcceptCommand.Execute(null);
                e.Handled = true;
                return;

            case Key.Escape:
                Close();
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Keeps the selection visible while arrowing past the bottom of the list - a selection
    /// the user cannot see is not a selection.</summary>
    private void ScrollToSelection()
    {
        if (ResultList.SelectedItem is { } selected)
        {
            ResultList.ScrollIntoView(selected);
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) =>
        (DataContext as CommandPaletteViewModel)?.AcceptCommand.Execute(null);
}
