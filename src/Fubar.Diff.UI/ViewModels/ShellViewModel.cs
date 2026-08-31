using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Core.Settings;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// The window: a set of comparison tabs, plus the things they share.
///
/// Deliberately thin. Everything about a comparison lives in <see cref="ComparisonViewModel"/>; what
/// belongs here is only what is genuinely shared - the theme, the settings file, and the recent list.
/// Each tab writing settings independently would race, with the last one to finish winning regardless
/// of which the user actually touched, so tabs raise events and the shell does the writing.
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly Func<ComparisonViewModel> _newTab;
    private readonly Func<MergeViewModel> _newMerge;
    private readonly Func<FolderViewModel> _newFolders;
    private readonly ISettingsStore _settingsStore;

    private AppSettings _settings = AppSettings.Default;

    public ShellViewModel(
        Func<ComparisonViewModel> newTab,
        Func<MergeViewModel> newMerge,
        Func<FolderViewModel> newFolders,
        ISettingsStore settingsStore,
        ThemeManagerViewModel themeManager)
    {
        _newTab = newTab;
        _newMerge = newMerge;
        _newFolders = newFolders;
        _settingsStore = settingsStore;
        ThemeManager = themeManager;

        _settings = settingsStore.Load();
        ThemeManager.Restore(_settings.Theme);

        // Entries whose files have since been deleted or moved are dropped: offering to reopen a file
        // that is not there produces an error the user did not ask for.
        Recent = [.. RecentComparisons.Prune(_settings.Recent, System.IO.File.Exists)];

        // The theme applies itself at startup from App; this only persists later changes, which it
        // cannot do itself without knowing about settings.
        ThemeManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeManagerViewModel.CurrentTheme))
            {
                Persist();
            }
        };
    }

    public ThemeManagerViewModel ThemeManager { get; }

    /// <summary>The open comparisons.</summary>
    public ObservableCollection<ComparisonViewModel> Tabs { get; } = [];

    /// <summary>The tab on screen. Everything in the window binds through this.</summary>
    [ObservableProperty]
    public partial ComparisonViewModel? SelectedTab { get; set; }

    /// <summary>Recently compared pairs, most recent first. Shared across tabs.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RecentComparison> Recent { get; set; } = [];

    // HasMultipleTabs used to live here, hiding the tab strip until a second tab existed - a strip
    // below the toolbar cost a row of window, so one tab was not worth it. The strip is IN the title
    // bar now, in space the window already spends on decoration, so there is nothing to buy back and
    // a single tab shows like any other.

    /// <summary>
    /// Opens the first tab and, if two files were named on the command line, compares them.
    /// </summary>
    public async Task InitializeAsync(StartupFiles startupFiles)
    {
        var tab = AddTab();
        await tab.InitializeAsync(startupFiles.Left, startupFiles.Right).ConfigureAwait(true);
    }

    /// <summary>Opens an empty tab and selects it.</summary>
    public ComparisonViewModel AddTab()
    {
        var tab = _newTab();
        tab.ApplyDefaults(_settings);

        tab.OptionsChanged += (_, _) => Persist();
        tab.ComparisonSucceeded += OnComparisonSucceeded;

        Tabs.Add(tab);
        SelectedTab = tab;

        return tab;
    }

    /// <summary>
    /// Closes a tab. The last one is emptied rather than removed - a window with no tabs has nothing
    /// to show and no way back.
    /// </summary>
    [RelayCommand]
    public async Task CloseTabAsync(ComparisonViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
        {
            return;
        }

        // Ask before anything is torn down. A tab holding typed changes is the only thing in this app
        // that can lose work by being closed, and the answer may well be "no, don't".
        if (!await tab.ConfirmDiscardAsync().ConfigureAwait(true))
        {
            return;
        }

        tab.ComparisonSucceeded -= OnComparisonSucceeded;

        // A tab owns a file-system watcher, which owns OS handles. Closing without this leaks one per
        // comparison for as long as the window is open.
        tab.Dispose();

        if (Tabs.Count == 1)
        {
            Tabs.Clear();
            AddTab();
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        // Select the neighbour rather than jumping to the first tab, which is what every tabbed
        // application does and what the user's eye expects.
        SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    /// <summary>
    /// Asks every tab whether it is alright to close, for the window shutting down.
    ///
    /// Stops at the first refusal and SELECTS that tab, so the user is looking at the thing they are
    /// being asked about rather than at whichever tab happened to be in front.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (tab.HasUnsavedEdits)
            {
                SelectedTab = tab;
            }

            if (!await tab.ConfirmDiscardAsync().ConfigureAwait(true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Opens a remembered pair in a new tab.</summary>
    [RelayCommand]
    private async Task OpenRecentAsync(RecentComparison? entry)
    {
        if (entry is null)
        {
            return;
        }

        // A new tab rather than replacing the current one: reopening something from history should not
        // discard unsaved merge decisions in whatever is already open.
        await AddTab().InitializeAsync(entry.Left, entry.Right).ConfigureAwait(true);
    }

    /// <summary>Opens an empty tab, for the toolbar button.</summary>
    [RelayCommand]
    private void NewTab() => AddTab();

    /// <summary>
    /// Builds a three-way merge, seeded with the persisted comparison and display preferences.
    ///
    /// Not a tab and not tracked here: a merge lives in its own window, has its own lifetime, and the
    /// shell's only stake in it is handing over the settings it should start from. The window itself
    /// owns it from then on.
    /// </summary>
    public MergeViewModel CreateMerge()
    {
        var merge = _newMerge();
        merge.ApplyDefaults(_settings);

        return merge;
    }

    /// <summary>
    /// Builds a folder comparison, seeded with the persisted defaults, and wires its "open this pair"
    /// event to a new tab.
    ///
    /// That wiring is the whole reason a folder comparison belongs to the shell rather than standing
    /// alone: it exists to lead somewhere. A folder comparison that could not open a file would be a
    /// listing, not a diff tool.
    /// </summary>
    public FolderViewModel CreateFolderComparison()
    {
        var folders = _newFolders();
        folders.ApplyDefaults(_settings);

        folders.CompareRequested += (_, request) =>
            _ = AddTab().InitializeAsync(request.LeftPath, request.RightPath);

        folders.OptionsChanged += (_, _) =>
        {
            _settings = folders.CaptureOptions(_settings);
            _ = _settingsStore.SaveAsync(_settings);
        };

        return folders;
    }

    /// <summary>Loads dropped files into the current tab, opening one if there is none.</summary>
    public Task OpenFilesAsync(IReadOnlyList<string> paths) =>
        (SelectedTab ?? AddTab()).OpenFilesAsync(paths);

    private void OnComparisonSucceeded(object? sender, EventArgs e)
    {
        if (sender is not ComparisonViewModel tab)
        {
            return;
        }

        Recent = RecentComparisons.Add(Recent, tab.LeftPath, tab.RightPath);
        Persist();
    }

    /// <summary>
    /// Writes the current preferences.
    ///
    /// Fire-and-forget: saving a preference is not something the user should ever wait for, and the
    /// store reports failure rather than throwing, so there is nothing to await for correctness.
    /// </summary>
    private void Persist()
    {
        _settings = (SelectedTab?.CaptureOptions(_settings) ?? _settings) with
        {
            Theme = ThemeManager.CurrentTheme.ToString(),
            Recent = Recent,
        };

        _ = _settingsStore.SaveAsync(_settings);
    }
}
