using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Settings;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Aggregates the Left Pane's otherwise-unrelated concerns - the workspace tree
/// (<see cref="WorkspaceExplorer"/>) and the Environments/Auth Profiles management groups
/// (<see cref="EnvironmentsSection"/>/<see cref="AuthProfilesSection"/>) - into one DataContext for
/// <c>LeftPaneView</c> (LeftPane.md §4.1), without coupling those view models to each other. The
/// active-environment selector is a shell-level control (MainViewModel.EnvironmentManager, bound in
/// MainWindow's control bar), so it deliberately isn't part of this aggregate.
/// </summary>
public sealed partial class LeftPaneViewModel : ViewModelBase
{
    private readonly IAppSettingsService? _settings;
    private bool _loading;

    public WorkspaceExplorerViewModel WorkspaceExplorer { get; }

    public EnvironmentsSectionViewModel EnvironmentsSection { get; }

    public AuthProfilesSectionViewModel AuthProfilesSection { get; }

    /// <summary>The occasions this workspace has a name for. Hidden entirely in a workspace that
    /// cannot have them - see <see cref="BatchesSectionViewModel"/>.</summary>
    public BatchesSectionViewModel BatchesSection { get; }

    /// <summary>
    /// Dark / Light / System.
    ///
    /// <para>No longer bound by this pane: it was a switcher pinned to the sidebar footer, and it is a
    /// row in the settings window now - theme is chosen about once, and that was the least-used
    /// control in the app holding the most permanent piece of chrome in the sidebar. Still reached
    /// through here, because the shell hangs <c>Initialize()</c> off the settings window's save to
    /// re-apply what was written (see <c>MainViewModel.CreateSettings</c>).</para>
    /// </summary>
    public ThemeManagerViewModel Theme { get; }

    /// <summary>
    /// Whether the Environments group is unfolded. Folded by default.
    ///
    /// <para>Environments and Auth Profiles are set up once and then chosen from the toolbar; the
    /// request tree is what this pane is for, and those two groups were taking roughly two hundred
    /// pixels off it in every session - one of them spending a whole row on "No auth profiles yet."
    /// Folded, they cost a line each and are one click from open.</para>
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnvironmentsExpanded { get; set; }

    /// <summary>Whether the Auth Profiles group is unfolded. Folded by default, as above.</summary>
    [ObservableProperty]
    public partial bool IsAuthProfilesExpanded { get; set; }

    /// <summary>Whether the Batches group is unfolded. Open by default, unlike the two above: a batch
    /// is something you run rather than something you set up once, so a folded list of them would be a
    /// folded list of the things this pane exists to start.</summary>
    [ObservableProperty]
    public partial bool IsBatchesExpanded { get; set; } = true;

    /// <summary>Settings are optional so the Gallery and tests can build this without a file.</summary>
    public LeftPaneViewModel(
        WorkspaceExplorerViewModel workspaceExplorer,
        EnvironmentsSectionViewModel environmentsSection,
        AuthProfilesSectionViewModel authProfilesSection,
        BatchesSectionViewModel batchesSection,
        ThemeManagerViewModel theme,
        IAppSettingsService? settings = null)
    {
        WorkspaceExplorer = workspaceExplorer;
        EnvironmentsSection = environmentsSection;
        AuthProfilesSection = authProfilesSection;
        BatchesSection = batchesSection;
        Theme = theme;
        _settings = settings;

        if (settings?.Load().Session is { } session)
        {
            // Suppressed, or restoring what was saved would immediately save it again - harmless here,
            // but it is a write to the settings file on every launch for no reason.
            _loading = true;
            IsEnvironmentsExpanded = session.EnvironmentsExpanded;
            IsAuthProfilesExpanded = session.AuthProfilesExpanded;
            _loading = false;
        }
    }

    partial void OnIsEnvironmentsExpandedChanged(bool value) => Persist();

    partial void OnIsAuthProfilesExpandedChanged(bool value) => Persist();

    /// <summary>
    /// Remembers which groups are open.
    ///
    /// <para>Worth persisting: a group that refolds itself every launch is more annoying than one that
    /// never folded, which would have made the whole change a net loss. Load-merge-save, never a fresh
    /// AppSettings - the open workspaces live in this same file.</para>
    /// </summary>
    private void Persist()
    {
        if (_loading || _settings is null)
        {
            return;
        }

        var settings = _settings.Load();
        settings.Session.EnvironmentsExpanded = IsEnvironmentsExpanded;
        settings.Session.AuthProfilesExpanded = IsAuthProfilesExpanded;
        _ = _settings.SaveAsync(settings);
    }
}
