using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Folders;
using Fubar.Diff.Core.Folders;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// Copying files between the two sides of a folder comparison.
///
/// This is the only thing in the app that writes a file the user did not name, so the tests that
/// matter most here are the refusals: nothing is written without an explicit yes, nothing is written
/// when there is no way to ASK, and a failure part-way through stops rather than pressing on.
/// </summary>
public class FolderCopyTests
{
    private sealed class StubService(FolderComparison result) : IFolderComparisonService
    {
        public int Calls { get; private set; }

        public Task<FolderComparison> CompareAsync(
            string leftRoot, string rightRoot, FolderComparisonOptions options,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult(result);
        }

        public Task<FolderComparison> CompareLinkedAsync(
            string root, FolderComparisonOptions options, IReadOnlyList<LinkRule> rules,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult(result);
        }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class Confirmation(bool answer) : IConfirmationService
    {
        public int Asked { get; private set; }

        public string? LastMessage { get; private set; }

        public string? LastTitle { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
        {
            Asked++;
            LastTitle = title;
            LastMessage = message;

            return Task.FromResult(answer);
        }
    }

    private sealed class Copier(string? failOn = null) : IFileCopier
    {
        public List<(string Source, string Destination)> Copies { get; } = [];

        public Task CopyAsync(string source, string destination, CancellationToken cancellationToken = default)
        {
            if (failOn is not null && source.EndsWith(failOn, StringComparison.Ordinal))
            {
                throw new FileCopyException(source, destination, "permission denied.");
            }

            Copies.Add((source, destination));

            return Task.CompletedTask;
        }
    }

    private static FolderEntry File(string name, FolderEntryStatus status) =>
        new(name, name, false, status, 1, 1, [])
        {
            LeftRelativePath = status == FolderEntryStatus.RightOnly ? null : name,
            RightRelativePath = status == FolderEntryStatus.LeftOnly ? null : name,
        };

    private static (FolderViewModel Folders, Copier Copier, Confirmation Confirm) Build(
        bool confirm,
        Copier? copier = null,
        params FolderEntry[] entries)
    {
        var service = new StubService(FolderComparison.Create(@"C:\left", @"C:\right", entries));
        var files = copier ?? new Copier();
        var confirmation = new Confirmation(confirm);

        var folders = new FolderViewModel(service, new NoPicker(), new ThemeManagerViewModel(), files, confirmation)
        {
            LeftPath = @"C:\left",
            RightPath = @"C:\right",
        };

        return (folders, files, confirmation);
    }

    /// <summary>Compares, then selects the row with the given name.</summary>
    private static async Task<FolderViewModel> Select(FolderViewModel folders, string name)
    {
        await folders.CompareAsync();

        var row = folders.Entries.First(r => r.Name == name);
        folders.SetSelection([row]);

        return folders;
    }

    [AvaloniaFact]
    public async Task A_confirmed_copy_writes_the_file()
    {
        var (folders, copier, confirm) = Build(confirm: true, null, File("a.txt", FolderEntryStatus.Different));
        await Select(folders, "a.txt");

        await folders.CopyToRightCommand.ExecuteAsync(null);

        Assert.Equal(1, confirm.Asked);
        Assert.Single(copier.Copies);
        Assert.Equal(Path.Combine(@"C:\left", "a.txt"), copier.Copies[0].Source);
        Assert.Equal(Path.Combine(@"C:\right", "a.txt"), copier.Copies[0].Destination);
    }

    [AvaloniaFact]
    public async Task Saying_no_writes_NOTHING()
    {
        // The refusal the whole feature rests on.
        var (folders, copier, confirm) = Build(confirm: false, null, File("a.txt", FolderEntryStatus.Different));
        await Select(folders, "a.txt");

        await folders.CopyToRightCommand.ExecuteAsync(null);

        Assert.Equal(1, confirm.Asked);
        Assert.Empty(copier.Copies);
        Assert.Contains("cancelled", folders.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Without_a_way_to_ask_nothing_is_offered_and_nothing_is_written()
    {
        // A host that wired up the copier but no confirmation must not get a window that silently
        // replaces files.
        var service = new StubService(FolderComparison.Create(@"C:\left", @"C:\right", [File("a.txt", FolderEntryStatus.Different)]));
        var copier = new Copier();

        var folders = new FolderViewModel(service, new NoPicker(), new ThemeManagerViewModel(), copier, confirmation: null)
        {
            LeftPath = @"C:\left",
            RightPath = @"C:\right",
        };

        await Select(folders, "a.txt");

        Assert.False(folders.CanCopy);
        Assert.False(folders.CanCopyToRight);

        await folders.CopyToRightCommand.ExecuteAsync(null);
        Assert.Empty(copier.Copies);
    }

    [AvaloniaFact]
    public async Task The_confirmation_says_that_a_file_will_be_REPLACED()
    {
        var (folders, _, confirm) = Build(confirm: false, null, File("a.txt", FolderEntryStatus.Different));
        await Select(folders, "a.txt");

        await folders.CopyToRightCommand.ExecuteAsync(null);

        Assert.Contains("Replace", confirm.LastTitle!, StringComparison.Ordinal);
        Assert.Contains("cannot be undone", confirm.LastMessage!, StringComparison.Ordinal);

        // And it names the exact path, so the user can check it before agreeing.
        Assert.Contains(Path.Combine(@"C:\right", "a.txt"), confirm.LastMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Creating_a_file_says_that_nothing_will_be_replaced()
    {
        // The other half: a copy that only adds is a much safer thing to agree to, and the dialog must
        // not cry wolf about it.
        var (folders, _, confirm) = Build(confirm: false, null, File("new.txt", FolderEntryStatus.LeftOnly));
        await Select(folders, "new.txt");

        await folders.CopyToRightCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Replace", confirm.LastTitle!, StringComparison.Ordinal);
        Assert.Contains("Nothing existing will be replaced", confirm.LastMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_direction_with_no_source_is_not_offered()
    {
        var (folders, _, _) = Build(confirm: true, null, File("new.txt", FolderEntryStatus.LeftOnly));
        await Select(folders, "new.txt");

        Assert.True(folders.CanCopyToRight);
        Assert.False(folders.CanCopyToLeft);
    }

    [AvaloniaFact]
    public async Task Identical_files_offer_no_copy_in_either_direction()
    {
        var (folders, _, _) = Build(confirm: true, null, File("same.txt", FolderEntryStatus.Same));
        await folders.CompareAsync();

        folders.ShowIdentical = true;
        folders.SetSelection([folders.Entries.First(r => r.Name == "same.txt")]);

        Assert.False(folders.CanCopyToRight);
        Assert.False(folders.CanCopyToLeft);
    }

    [AvaloniaFact]
    public async Task The_button_says_how_many_files_and_how_many_it_replaces()
    {
        var tree = new FolderEntry("src", "src", true, FolderEntryStatus.Different, -1, -1,
        [
            File("src/a.txt", FolderEntryStatus.Different),
            File("src/b.txt", FolderEntryStatus.Different),
            File("src/new.txt", FolderEntryStatus.LeftOnly),
        ]);

        var (folders, _, _) = Build(confirm: true, null, tree);
        await Select(folders, "src");

        Assert.Equal("Copy 3 files to the right, replacing 2", folders.CopyToRightDescription);
    }

    [AvaloniaFact]
    public async Task A_folder_copies_everything_under_it()
    {
        var tree = new FolderEntry("src", "src", true, FolderEntryStatus.Different, -1, -1,
        [
            File("src/a.txt", FolderEntryStatus.Different),
            File("src/b.txt", FolderEntryStatus.Different),
            File("src/same.txt", FolderEntryStatus.Same),
        ]);

        var (folders, copier, _) = Build(confirm: true, null, tree);
        await Select(folders, "src");

        await folders.CopyToRightCommand.ExecuteAsync(null);

        // The identical one is not written: there is nothing to copy, and touching it would change a
        // timestamp for no reason.
        Assert.Equal(2, copier.Copies.Count);
    }

    [AvaloniaFact]
    public async Task A_failure_part_way_through_stops_and_says_so()
    {
        // The rest of the batch probably shares the same permission or the same disk, and a partial
        // copy the user was not told about is worse than one that stopped and named the file.
        var tree = new FolderEntry("src", "src", true, FolderEntryStatus.Different, -1, -1,
        [
            File("src/a.txt", FolderEntryStatus.Different),
            File("src/locked.txt", FolderEntryStatus.Different),
            File("src/c.txt", FolderEntryStatus.Different),
        ]);

        var (folders, copier, _) = Build(confirm: true, new Copier(failOn: "locked.txt"), tree);
        await Select(folders, "src");

        await folders.CopyToRightCommand.ExecuteAsync(null);

        Assert.Single(copier.Copies);
        Assert.NotNull(folders.ErrorMessage);
        Assert.Contains("locked.txt", folders.ErrorMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task The_tree_is_re_walked_afterwards()
    {
        // Without it the rows the user has just made identical would still show as different, which
        // reads as the copy having failed.
        var service = new StubService(FolderComparison.Create(@"C:\left", @"C:\right", [File("a.txt", FolderEntryStatus.Different)]));

        var folders = new FolderViewModel(
            service, new NoPicker(), new ThemeManagerViewModel(), new Copier(), new Confirmation(true))
        {
            LeftPath = @"C:\left",
            RightPath = @"C:\right",
        };

        await folders.CompareAsync();
        folders.SetSelection([folders.Entries.First()]);

        var before = service.Calls;
        await folders.CopyToRightCommand.ExecuteAsync(null);

        Assert.Equal(before + 1, service.Calls);
    }

    [AvaloniaFact]
    public async Task Nothing_selected_means_nothing_to_copy()
    {
        var (folders, copier, confirm) = Build(confirm: true, null, File("a.txt", FolderEntryStatus.Different));
        await folders.CompareAsync();

        await folders.CopyToRightCommand.ExecuteAsync(null);

        Assert.Equal(0, confirm.Asked);
        Assert.Empty(copier.Copies);
    }

    [AvaloniaFact]
    public async Task A_snapshot_is_accepted_by_copying_it_over_its_baseline()
    {
        // One-folder mode: both roots are the same directory and the pair differs by NAME. This is the
        // "accept this .received" action, and the reason snapshot review wanted copying at all.
        var entry = new FolderEntry("Thing.json", "Thing.json", false, FolderEntryStatus.Different, 1, 1, [])
        {
            LeftRelativePath = "Thing.verified.json",
            RightRelativePath = "Thing.received.json",
        };

        var service = new StubService(FolderComparison.Create(@"C:\snaps", @"C:\snaps", [entry]));
        var copier = new Copier();

        var folders = new FolderViewModel(
            service, new NoPicker(), new ThemeManagerViewModel(), copier, new Confirmation(true))
        {
            LeftPath = @"C:\snaps",
            LinkedMode = true,
        };

        await folders.CompareAsync();
        folders.SetSelection([folders.Entries.First()]);

        await folders.CopyToLeftCommand.ExecuteAsync(null);

        Assert.Single(copier.Copies);
        Assert.EndsWith("Thing.received.json", copier.Copies[0].Source, StringComparison.Ordinal);
        Assert.EndsWith("Thing.verified.json", copier.Copies[0].Destination, StringComparison.Ordinal);
    }
}
