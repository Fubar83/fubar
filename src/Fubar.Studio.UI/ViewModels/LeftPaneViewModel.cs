namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Aggregates the Left Pane's otherwise-unrelated concerns - the workspace tree
/// (<see cref="WorkspaceExplorer"/>) and the Environments/Auth Profiles management groups
/// (<see cref="EnvironmentsSection"/>/<see cref="AuthProfilesSection"/>) - into one DataContext for
/// <c>LeftPaneView</c> (LeftPane.md §4.1), without coupling those view models to each other. The
/// active-environment selector is a shell-level control (MainViewModel.EnvironmentManager, bound in
/// MainWindow's control bar), so it deliberately isn't part of this aggregate.
/// </summary>
public sealed class LeftPaneViewModel : ViewModelBase
{
    public WorkspaceExplorerViewModel WorkspaceExplorer { get; }

    public EnvironmentsSectionViewModel EnvironmentsSection { get; }

    public AuthProfilesSectionViewModel AuthProfilesSection { get; }

    public LeftPaneViewModel(
        WorkspaceExplorerViewModel workspaceExplorer,
        EnvironmentsSectionViewModel environmentsSection,
        AuthProfilesSectionViewModel authProfilesSection)
    {
        WorkspaceExplorer = workspaceExplorer;
        EnvironmentsSection = environmentsSection;
        AuthProfilesSection = authProfilesSection;
    }
}
