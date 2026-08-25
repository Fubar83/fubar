using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

public partial class LeftPaneView : UserControl
{
    public LeftPaneView()
    {
        InitializeComponent();
    }

    // Environments/Auth Profiles rows: tapping the row opens it for editing, but the row also
    // hosts its own Rename/Delete buttons - since Avalonia's Tapped gesture bubbles independently
    // of a descendant Button's own Click handling, a tap that landed on one of those buttons would
    // otherwise ALSO open the editor. OriginatedFromButton guards against that double-action.
    private static bool OriginatedFromButton(TappedEventArgs e) =>
        e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() is not null);

    private void EnvironmentRow_OnTapped(object? sender, TappedEventArgs e)
    {
        if (OriginatedFromButton(e))
        {
            return;
        }

        if (sender is Control { DataContext: EnvironmentRowViewModel { IsEditing: false } row } && DataContext is LeftPaneViewModel vm)
        {
            vm.EnvironmentsSection.EditCommand.Execute(row);
        }
    }

    private void EnvironmentEditNameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: EnvironmentRowViewModel row } || DataContext is not LeftPaneViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                vm.EnvironmentsSection.CommitRenameCommand.Execute(row);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.EnvironmentsSection.CancelRenameCommand.Execute(row);
                e.Handled = true;
                break;
        }
    }

    private void EnvironmentEditNameTextBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: EnvironmentRowViewModel row } && DataContext is LeftPaneViewModel vm)
        {
            vm.EnvironmentsSection.CommitRenameCommand.Execute(row);
        }
    }

    private void AuthProfileRow_OnTapped(object? sender, TappedEventArgs e)
    {
        if (OriginatedFromButton(e))
        {
            return;
        }

        if (sender is Control { DataContext: AuthProfileRowViewModel { IsEditing: false } row } && DataContext is LeftPaneViewModel vm)
        {
            vm.AuthProfilesSection.EditCommand.Execute(row);
        }
    }

    private void AuthProfileEditNameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: AuthProfileRowViewModel row } || DataContext is not LeftPaneViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                vm.AuthProfilesSection.CommitRenameCommand.Execute(row);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.AuthProfilesSection.CancelRenameCommand.Execute(row);
                e.Handled = true;
                break;
        }
    }

    private void AuthProfileEditNameTextBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: AuthProfileRowViewModel row } && DataContext is LeftPaneViewModel vm)
        {
            vm.AuthProfilesSection.CommitRenameCommand.Execute(row);
        }
    }
}
