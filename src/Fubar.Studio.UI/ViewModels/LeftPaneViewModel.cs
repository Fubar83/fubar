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

    /// <summary>
    /// The Dark / Light / System switcher this pane's header has specified since LeftPane.md §4.1 was
    /// written.
    ///
    /// <para><see cref="ThemeManagerViewModel"/> was complete from the start - it applies instantly,
    /// persists, and its own summary tells you to bind <c>CurrentTheme</c> two-way - and the string
    /// "ThemeManager" appeared in no <c>.axaml</c> in the repository. The theme could be persisted and
    /// applied, and never chosen. Third documented instance of built-and-never-wired here, which is
    /// why <c>WiringTests</c> now exists.</para>
    /// </summary>
    public ThemeManagerViewModel Theme { get; }

    public LeftPaneViewModel(
        WorkspaceExplorerViewModel workspaceExplorer,
        EnvironmentsSectionViewModel environmentsSection,
        AuthProfilesSectionViewModel authProfilesSection,
        ThemeManagerViewModel theme)
    {
        WorkspaceExplorer = workspaceExplorer;
        EnvironmentsSection = environmentsSection;
        AuthProfilesSection = authProfilesSection;
        Theme = theme;
    }
}
