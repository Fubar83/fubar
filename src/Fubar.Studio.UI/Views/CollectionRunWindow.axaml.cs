using Avalonia.Controls;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

/// <summary>
/// The Run window. Deliberately NOT modal and NOT owned-modal: a run can take minutes, and a dialog
/// that blocks the rest of the app for its duration would stop you reading the request it is stuck on.
/// </summary>
public partial class CollectionRunWindow : Window
{
    // No hand-written InitializeComponent. The XAML compiler generates one that also assigns the
    // x:Name fields; overriding it leaves them null (see CLAUDE.md).
    public CollectionRunWindow()
    {
        InitializeComponent();
    }

    public CollectionRunWindow(CollectionRunViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Title = $"Run — {viewModel.Target}";
    }
}
