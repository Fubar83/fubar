using Avalonia.Controls;
using Avalonia.Input;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

public partial class WorkspaceExplorerView : UserControl
{
    public WorkspaceExplorerView()
    {
        InitializeComponent();
    }

    // The inline-rename TextBox lives inside the TreeView's per-node DataTemplate, so its
    // DataContext is the WorkspaceNodeViewModel being renamed - not the WorkspaceExplorerViewModel
    // that owns the Commit/Cancel commands. Handling Enter/Escape/LostFocus here in code-behind
    // avoids the awkward cross-template compiled-binding path that would otherwise be needed to
    // wire a per-item TextBox to a command on the UserControl's own DataContext.
    private void EditNameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: WorkspaceNodeViewModel node } || DataContext is not WorkspaceExplorerViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                vm.CommitRenameCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CancelRenameCommand.Execute(node);
                e.Handled = true;
                break;
        }
    }

    private void EditNameTextBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: WorkspaceNodeViewModel node } && DataContext is WorkspaceExplorerViewModel vm)
        {
            vm.CommitRenameCommand.Execute(node);
        }
    }

    // Single-tapping while an inline rename edit is in progress (a click could land on the
    // still-focused TextBox) shouldn't also try to open the item as a request tab. Folders just
    // select/expand as normal - ActivateSelectionCommand only acts on request files (IsDirectory:
    // false), matching how Environments/Auth Profiles rows already open on a single click.
    private void TreeView_OnTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is WorkspaceExplorerViewModel { SelectedNode.IsEditing: false } vm)
        {
            vm.ActivateSelectionCommand.Execute(null);
        }
    }

    /// <summary>
    /// Right-clicking a row selects it first, so the menu acts on what is under the pointer.
    ///
    /// <para>Every command in the flyout reads <c>SelectedNode</c>, and the flyout is attached to the
    /// TREE rather than to a row - so right-clicking one request and choosing Delete deleted a
    /// different one: whichever happened to be selected. That is the worst possible version of this
    /// bug, because the menu appears next to the row you aimed at.</para>
    ///
    /// <para>Selecting on right-click is what Explorer, Finder and VS Code all do, so the fix is also
    /// the behaviour people already expect. Right-clicking empty space below the tree clears the
    /// selection instead, which is the same convention - and it makes New Request there create at the
    /// workspace root rather than inside whatever was last clicked.</para>
    ///
    /// <para>Tunnelling, because the flyout opens on the bubbling pass: selecting afterwards would be
    /// selecting after the menu had already decided what it applied to.</para>
    /// </summary>
    private void TreeView_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not WorkspaceExplorerViewModel vm
            || !e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        // An in-progress rename owns the pointer; stealing the selection would commit it by side
        // effect, from a gesture that was not asking to.
        if (vm.SelectedNode is { IsEditing: true })
        {
            return;
        }

        vm.SelectedNode = (e.Source as Control)?.DataContext as WorkspaceNodeViewModel;
    }
}
