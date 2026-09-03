using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// Toggling an option has to reach the settings file.
///
/// Reported as options that work for the session and are gone after a restart - and the settings file
/// really was a day old while the toolbar showed things switched on. The store itself saves correctly
/// and the folder is writable, so whatever breaks is in the chain between the toggle and the save, which
/// is what these cover. It is the kind of failure that is invisible until you restart, and
/// <c>SaveAsync</c> is deliberately built never to throw, so nothing announces it.
/// </summary>
public class SettingsPersistenceTests
{
    private sealed class RecordingStore : ISettingsStore
    {
        public int Saves { get; private set; }

        public AppSettings Last { get; private set; } = AppSettings.Default;

        public AppSettings Load() => AppSettings.Default;

        public Task<bool> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Saves++;
            Last = settings;

            return Task.FromResult(true);
        }
    }

    private sealed class Comparisons : IFileComparisonService
    {
        public Task<FileComparison> CompareFilesAsync(string left, string right, ComparisonOptions options, CancellationToken ct = default) =>
            Task.FromResult(FileComparison.Empty);

        public Task<FileComparison> CompareTextAsync(string leftText, string rightText, ComparisonOptions options, string leftLabel = "left", string rightLabel = "right", CancellationToken ct = default) =>
            Task.FromResult(FileComparison.Empty);

        public Task<FileComparison> CompareDocumentsAsync(TextDocument left, TextDocument right, ComparisonOptions options, CancellationToken ct = default) =>
            Task.FromResult(new FileComparison(left, right, options, DiffResult.Empty));

        public Task<FileComparison> RecompareAsync(FileComparison comparison, ComparisonOptions options, CancellationToken ct = default) =>
            Task.FromResult(comparison);

        public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) => comparison;

        public JsonDisplay FormatJsonForDisplay(FileComparison comparison, bool prettyLeft, bool prettyRight, Core.Json.JsonFormatOptions format) =>
            new(comparison.OriginalLeftText, comparison.OriginalRightText, comparison.OriginalSemanticChanges);
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickFilesAsync(string title) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class NoWatcher : IFileChangeWatcher
    {
        public event EventHandler? Changed;

        public void Watch(IReadOnlyList<string> paths) => _ = Changed;

        public void Stop() { }

        public void Dispose() { }
    }

    private sealed class NoClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class NoWriter : ITextFileWriter
    {
        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private static (ShellViewModel Shell, RecordingStore Store) Build()
    {
        var store = new RecordingStore();
        var theme = new ThemeManagerViewModel();

        ComparisonViewModel NewTab() => new(
            new Comparisons(), new MergeService(new NoWriter()), new NoPicker(), new NoWatcher(),
            new NoClipboard(), new NoWriter(), theme, null);

        var shell = new ShellViewModel(
            NewTab,
            () => throw new NotSupportedException(),
            () => throw new NotSupportedException(),
            store,
            theme,
            new NoPicker());

        return (shell, store);
    }

    [AvaloniaFact]
    public void Toggling_an_option_saves()
    {
        var (shell, store) = Build();
        var tab = shell.AddTab();
        var before = store.Saves;

        tab.NormalizeStructure = true;

        Assert.True(store.Saves > before, "toggling Reformat should have saved");
        Assert.True(store.Last.NormalizeStructure);
    }

    [AvaloniaFact]
    public void Every_option_that_the_toolbar_and_settings_expose_saves()
    {
        // One case per toggle rather than a single representative: they are wired individually, so one
        // of them silently not being wired is exactly the shape of bug that hides here.
        var (shell, _) = Build();
        var tab = shell.AddTab();

        var toggles = new (string Name, Action Set, Func<AppSettings, bool> Read)[]
        {
            ("IgnoreWhitespace", () => tab.IgnoreWhitespace = true, s => s.IgnoreWhitespace),
            ("IgnoreCase", () => tab.IgnoreCase = true, s => s.IgnoreCase),
            ("NormalizeStructure", () => tab.NormalizeStructure = true, s => s.NormalizeStructure),
            ("CollapseUnchanged", () => tab.CollapseUnchanged = false, s => !s.CollapseUnchanged),
            ("IgnoreComments", () => tab.IgnoreComments = true, s => s.IgnoreComments),
            ("IsEditing", () => tab.IsEditing = true, s => s.Editing),
            ("MatchArraysByPosition", () => tab.MatchArraysByPosition = true, s => s.MatchArraysByPosition),
        };

        foreach (var (name, set, read) in toggles)
        {
            var store = (RecordingStore)Store(shell);
            var before = store.Saves;

            set();

            Assert.True(store.Saves > before, $"{name} did not save");
            Assert.True(read(store.Last), $"{name} saved the wrong value");
        }
    }

    [AvaloniaFact]
    public void A_duplicate_array_key_override_does_not_kill_persistence()
    {
        // CaptureOptions builds a dictionary from the override list. ToDictionary throws on a duplicate
        // key, and it is called from inside an event handler - so one duplicate takes the exception out
        // through the toggle that raised it and nothing is ever saved again for the rest of the session.
        // The Settings window's Add button had no duplicate check, so this was reachable by typing the
        // same path twice.
        var (shell, store) = Build();
        var tab = shell.AddTab();

        tab.ArrayKeyOverrides.Add(new ArrayKeyOverrideEntry("$.items", "id"));
        tab.ArrayKeyOverrides.Add(new ArrayKeyOverrideEntry("$.items", "sku"));

        var before = store.Saves;
        tab.IgnoreCase = true;

        Assert.True(store.Saves > before, "a duplicate override must not stop settings saving");
    }

    private static ISettingsStore Store(ShellViewModel shell) =>
        (ISettingsStore)typeof(ShellViewModel)
            .GetField("_settingsStore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(shell)!;
}
