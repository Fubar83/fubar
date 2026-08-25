using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One row in the Left Pane's Auth Profiles management group - wraps an
/// <see cref="AuthProfile"/> with inline-rename state, same pattern as <c>WorkspaceNodeViewModel</c>.</summary>
public partial class AuthProfileRowViewModel : ViewModelBase
{
    public AuthProfileRowViewModel(AuthProfile model)
    {
        Model = model;
        Name = model.Name;
    }

    public AuthProfile Model { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; } = "";
}
