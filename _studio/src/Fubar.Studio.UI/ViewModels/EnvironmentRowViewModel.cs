using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One row in the Left Pane's Environments management group (LeftPane.md §4.1) - wraps a
/// <see cref="WorkspaceEnvironment"/> with inline-rename state, same pattern as <c>WorkspaceNodeViewModel</c>.</summary>
public partial class EnvironmentRowViewModel : ViewModelBase
{
    public EnvironmentRowViewModel(WorkspaceEnvironment model)
    {
        Model = model;
        Name = model.Name;
    }

    public WorkspaceEnvironment Model { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; } = "";
}
