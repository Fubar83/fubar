using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Folders;
using Fubar.Diff.Core.Folders;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// The folder comparison window's own behaviour: filtering, and leading somewhere.
///
/// The filtering is the part that decides whether the feature is usable at all. On two real checkouts
/// the identical files are most of the tree, and an answer to "what differs" delivered inside ten
/// thousand files that do not is not an answer.
/// </summary>
public class FolderViewModelTests
{
    private sealed class StubService(FolderComparison result) : IFolderComparisonService
    {
        public int Calls { get; private set; }

        public Task<FolderComparison> CompareAsync(
            string leftRoot, string rightRoot, FolderComparisonOptions options,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Options = options;

            return Task.FromResult(result);
        }

        public FolderComparisonOptions? Options { get; private set; }

        public string? LinkedRoot { get; private set; }

        public IReadOnlyList<LinkRule>? Rules { get; private set; }

        public Task<FolderComparison> CompareLinkedAsync(
            string root, FolderComparisonOptions options, IReadOnlyList<LinkRule> rules,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Options = options;
            LinkedRoot = root;
            Rules = rules;

            return Task.FromResult(result);
        }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static FolderEntry File(string name, FolderEntryStatus status) =>
        new(name, name, false, status, 1, 1, [])
        {
            LeftRelativePath = status == FolderEntryStatus.RightOnly ? null : name,
            RightRelativePath = status == FolderEntryStatus.LeftOnly ? null : name,
        };

    private static FolderEntry Directory(string name, params FolderEntry[] children) =>
        new(name, name, true,
            children.Any(c => c.Status != FolderEntryStatus.Same)
                ? FolderEntryStatus.Different
                : FolderEntryStatus.Same,
            FolderEntry.NoSize, FolderEntry.NoSize, children);

    private static (FolderViewModel Folders, StubService Service) Build(params FolderEntry[] entries)
    {
        var service = new StubService(FolderComparison.Create(@"C:\left", @"C:\right", entries));

        var folders = new FolderViewModel(service, new NoPicker(), new ThemeManagerViewModel())
        {
            LeftPath = @"C:\left",
            RightPath = @"C:\right",
        };

        return (folders, service);
    }

    private static IEnumerable<FolderEntryViewModel> Flatten(IReadOnlyList<FolderEntryViewModel> rows)
    {
        foreach (var row in rows)
        {
            yield return row;

            foreach (var child in Flatten(row.Children))
            {
                yield return child;
            }
        }
    }

    [AvaloniaFact]
    public async Task Identical_files_are_hidden_by_default()
    {
        var (folders, _) = Build(
            File("same.txt", FolderEntryStatus.Same),
            File("changed.txt", FolderEntryStatus.Different));

        await folders.CompareAsync();

        Assert.Equal(["changed.txt"], Flatten(folders.Entries).Select(r => r.Name));
    }

    [AvaloniaFact]
    public async Task Showing_identical_files_reveals_them()
    {
        var (folders, _) = Build(
            File("same.txt", FolderEntryStatus.Same),
            File("changed.txt", FolderEntryStatus.Different));

        await folders.CompareAsync();
        folders.ShowIdentical = true;

        Assert.Equal(2, Flatten(folders.Entries).Count());
    }

    [AvaloniaFact]
    public async Task A_folder_holding_only_identical_files_is_hidden_too()
    {
        // Whether a folder is worth showing is a fact about its CONTENTS, which is why the tree is
        // rebuilt bottom-up rather than filtered per row.
        var (folders, _) = Build(
            Directory("quiet", File("a.txt", FolderEntryStatus.Same)),
            Directory("noisy", File("b.txt", FolderEntryStatus.Different)));

        await folders.CompareAsync();

        Assert.Equal(["noisy", "b.txt"], Flatten(folders.Entries).Select(r => r.Name));
    }

    [AvaloniaFact]
    public async Task A_folder_survives_when_anything_inside_it_differs_however_deep()
    {
        var (folders, _) = Build(
            Directory("a", Directory("b", Directory("c", File("deep.txt", FolderEntryStatus.LeftOnly)))));

        await folders.CompareAsync();

        Assert.Equal(["a", "b", "c", "deep.txt"], Flatten(folders.Entries).Select(r => r.Name));
    }

    [AvaloniaFact]
    public async Task The_status_line_says_how_many_identical_files_were_hidden()
    {
        // Nothing may go missing silently: the filter is aggressive, so the count of what it removed
        // has to be on screen.
        var (folders, _) = Build(
            File("same.txt", FolderEntryStatus.Same),
            File("changed.txt", FolderEntryStatus.Different));

        await folders.CompareAsync();

        Assert.Contains("1 identical file(s) hidden", folders.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Matching_folders_say_so_rather_than_showing_an_empty_tree()
    {
        var (folders, _) = Build(File("same.txt", FolderEntryStatus.Same));

        await folders.CompareAsync();

        Assert.True(folders.AreIdentical);
        Assert.Contains("match", folders.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Nothing_happens_until_both_folders_are_chosen()
    {
        var service = new StubService(FolderComparison.Empty);
        var folders = new FolderViewModel(service, new NoPicker(), new ThemeManagerViewModel())
        {
            LeftPath = @"C:\left",
        };

        await folders.CompareAsync();

        Assert.Equal(0, service.Calls);
    }

    // ---- Opening a pair -------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Opening_a_pair_asks_for_absolute_paths_on_both_sides()
    {
        // The event the shell turns into a tab. A folder comparison that could not open a file would
        // be a listing rather than a diff tool.
        var (folders, _) = Build(Directory("src", File("app.cs", FolderEntryStatus.Different)));

        await folders.CompareAsync();

        FileComparisonRequest? request = null;
        folders.CompareRequested += (_, r) => request = r;

        folders.SelectedEntry = Flatten(folders.Entries).Single(r => r.CanCompare);
        folders.OpenCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(Path.Combine(@"C:\left", "app.cs"), request!.LeftPath);
        Assert.Equal(Path.Combine(@"C:\right", "app.cs"), request.RightPath);
    }

    [AvaloniaFact]
    public async Task A_file_only_one_side_has_cannot_be_opened()
    {
        var (folders, _) = Build(File("only-left.txt", FolderEntryStatus.LeftOnly));

        await folders.CompareAsync();

        var raised = false;
        folders.CompareRequested += (_, _) => raised = true;

        folders.SelectedEntry = Flatten(folders.Entries).Single();
        folders.OpenCommand.Execute(null);

        Assert.False(folders.SelectedEntry.CanCompare);
        Assert.False(raised);
    }

    [AvaloniaFact]
    public async Task A_directory_cannot_be_opened_as_a_file_diff()
    {
        var (folders, _) = Build(Directory("src", File("a.cs", FolderEntryStatus.Different)));

        await folders.CompareAsync();

        var raised = false;
        folders.CompareRequested += (_, _) => raised = true;

        folders.SelectedEntry = Flatten(folders.Entries).First(r => r.IsDirectory);
        folders.OpenCommand.Execute(null);

        Assert.False(raised);
    }

    [AvaloniaFact]
    public void Opening_with_nothing_selected_does_nothing()
    {
        var (folders, _) = Build();

        var raised = false;
        folders.CompareRequested += (_, _) => raised = true;

        folders.OpenCommand.Execute(null);

        Assert.False(raised);
    }

    // ---- Pairing two files by hand --------------------------------------------------------------

    /// <summary>A rename: the old name exists only on the left, the new one only on the right.</summary>
    private static async Task<FolderViewModel> Renamed()
    {
        var (folders, _) = Build(
            File("old-name.cs", FolderEntryStatus.LeftOnly),
            File("new-name.cs", FolderEntryStatus.RightOnly));

        await folders.CompareAsync();

        return folders;
    }

    private static FolderEntryViewModel Row(FolderViewModel folders, string name) =>
        Flatten(folders.Entries).Single(r => r.Name == name);

    [AvaloniaFact]
    public async Task Neither_half_of_a_rename_can_be_opened_on_its_own()
    {
        // Which is the whole reason manual pairing exists.
        var folders = await Renamed();

        Assert.False(Row(folders, "old-name.cs").CanCompare);
        Assert.False(Row(folders, "new-name.cs").CanCompare);
    }

    [AvaloniaFact]
    public async Task Two_selected_files_from_opposite_sides_can_be_compared()
    {
        var folders = await Renamed();

        FileComparisonRequest? request = null;
        folders.CompareRequested += (_, r) => request = r;

        folders.SetSelection([Row(folders, "old-name.cs"), Row(folders, "new-name.cs")]);

        Assert.True(folders.CanComparePair);

        folders.ComparePairCommand.Execute(null);

        Assert.Equal(Path.Combine(@"C:\left", "old-name.cs"), request!.LeftPath);
        Assert.Equal(Path.Combine(@"C:\right", "new-name.cs"), request.RightPath);
    }

    [AvaloniaFact]
    public async Task Selecting_them_the_other_way_round_pairs_them_the_same_way()
    {
        // Two files that each exist on one side only have exactly one sensible pairing, whichever
        // order they were clicked in.
        var folders = await Renamed();

        FileComparisonRequest? request = null;
        folders.CompareRequested += (_, r) => request = r;

        folders.SetSelection([Row(folders, "new-name.cs"), Row(folders, "old-name.cs")]);
        folders.ComparePairCommand.Execute(null);

        Assert.Equal(Path.Combine(@"C:\left", "old-name.cs"), request!.LeftPath);
        Assert.Equal(Path.Combine(@"C:\right", "new-name.cs"), request.RightPath);
    }

    [AvaloniaFact]
    public async Task With_two_files_present_on_both_sides_the_first_selected_becomes_the_left()
    {
        // Genuinely ambiguous, so selection order decides rather than the tool guessing.
        var (folders, _) = Build(
            File("a.cs", FolderEntryStatus.Different),
            File("b.cs", FolderEntryStatus.Different));

        await folders.CompareAsync();

        FileComparisonRequest? request = null;
        folders.CompareRequested += (_, r) => request = r;

        folders.SetSelection([Row(folders, "b.cs"), Row(folders, "a.cs")]);
        folders.ComparePairCommand.Execute(null);

        Assert.Equal(Path.Combine(@"C:\left", "b.cs"), request!.LeftPath);
        Assert.Equal(Path.Combine(@"C:\right", "a.cs"), request.RightPath);
    }

    [AvaloniaFact]
    public async Task Two_files_from_the_SAME_side_cannot_be_paired()
    {
        // There is no comparison to make: nothing would supply the other side.
        var (folders, _) = Build(
            File("one.cs", FolderEntryStatus.LeftOnly),
            File("two.cs", FolderEntryStatus.LeftOnly));

        await folders.CompareAsync();
        folders.SetSelection([Row(folders, "one.cs"), Row(folders, "two.cs")]);

        Assert.False(folders.CanComparePair);
    }

    [AvaloniaFact]
    public async Task A_directory_cannot_be_half_of_a_pair()
    {
        var (folders, _) = Build(
            Directory("src", File("a.cs", FolderEntryStatus.LeftOnly)),
            File("b.cs", FolderEntryStatus.RightOnly));

        await folders.CompareAsync();
        folders.SetSelection([Row(folders, "src"), Row(folders, "b.cs")]);

        Assert.False(folders.CanComparePair);
    }

    [AvaloniaFact]
    public async Task Fewer_or_more_than_two_selected_rows_is_not_a_pair()
    {
        var folders = await Renamed();

        folders.SetSelection([Row(folders, "old-name.cs")]);
        Assert.False(folders.CanComparePair);

        folders.SetSelection([]);
        Assert.False(folders.CanComparePair);

        folders.SetSelection([Row(folders, "old-name.cs"), Row(folders, "new-name.cs"), Row(folders, "old-name.cs")]);
        Assert.False(folders.CanComparePair);
    }

    [AvaloniaFact]
    public async Task The_button_names_the_pair_it_would_open()
    {
        var folders = await Renamed();

        folders.SetSelection([Row(folders, "old-name.cs"), Row(folders, "new-name.cs")]);

        Assert.Equal("Compare old-name.cs ↔ new-name.cs", folders.PairDescription);
    }

    [AvaloniaFact]
    public async Task Comparing_a_pair_that_does_not_resolve_does_nothing()
    {
        var folders = await Renamed();

        var raised = false;
        folders.CompareRequested += (_, _) => raised = true;

        folders.SetSelection([Row(folders, "old-name.cs")]);
        folders.ComparePairCommand.Execute(null);

        Assert.False(raised);
    }

    [AvaloniaFact]
    public async Task Selecting_one_row_still_sets_the_single_selection()
    {
        // The ordinary path - double-click and Enter both act on it.
        var folders = await Renamed();

        folders.SetSelection([Row(folders, "old-name.cs")]);

        Assert.Same(Row(folders, "old-name.cs"), folders.SelectedEntry);
    }

    // ---- One folder, linked by name -------------------------------------------------------------

    [AvaloniaFact]
    public async Task Linked_mode_walks_one_folder_with_the_rules()
    {
        var (folders, service) = Build();
        folders.LinkedMode = true;
        folders.RightPath = string.Empty;

        await folders.CompareAsync();

        Assert.Equal(@"C:\left", service.LinkedRoot);
        Assert.Contains(service.Rules!, r => r.Left == ".verified" && r.Right == ".received");
    }

    [AvaloniaFact]
    public async Task Linked_mode_needs_only_one_folder()
    {
        // The right-hand picker is not just hidden - it is genuinely not required.
        var (folders, service) = Build();
        folders.RightPath = string.Empty;

        await folders.CompareAsync();
        Assert.Equal(0, service.Calls);

        folders.LinkedMode = true;
        await folders.CompareAsync();

        Assert.Equal(1, service.Calls);
    }

    [AvaloniaFact]
    public async Task Edited_rules_reach_the_comparison()
    {
        var (folders, service) = Build();
        folders.LinkedMode = true;
        folders.LinkRuleText = ".baseline = .current";

        await folders.CompareAsync();

        var rule = Assert.Single(service.Rules!);
        Assert.Equal(".baseline", rule.Left);
        Assert.Equal(".current", rule.Right);
    }

    [AvaloniaFact]
    public void The_right_hand_picker_disappears_in_linked_mode()
    {
        var (folders, _) = Build();

        Assert.True(folders.IsTwoFolderMode);
        Assert.Equal("Left folder", folders.LeftFolderHeader);

        folders.LinkedMode = true;

        Assert.False(folders.IsTwoFolderMode);
        Assert.Equal("Folder", folders.LeftFolderHeader);
    }

    [AvaloniaFact]
    public async Task The_status_line_is_phrased_for_one_folder()
    {
        // "Only on the left" is meaningless when there is one folder. What those counts mean here is a
        // new snapshot and a snapshot nothing produces any more.
        var (folders, _) = Build(
            File("new.json", FolderEntryStatus.RightOnly),
            File("stale.json", FolderEntryStatus.LeftOnly));

        folders.LinkedMode = true;
        await folders.CompareAsync();

        Assert.Contains("1 new", folders.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("no new output", folders.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("only on the left", folders.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task A_clean_linked_run_says_there_is_nothing_to_review()
    {
        var (folders, _) = Build(File("a.json", FolderEntryStatus.Same));
        folders.LinkedMode = true;

        await folders.CompareAsync();

        Assert.Contains("Nothing to review", folders.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_mode_and_rules_are_remembered()
    {
        var (folders, _) = Build();

        folders.ApplyDefaults(AppSettings.Default with
        {
            FolderLinkedMode = true,
            FolderLinkRules = [".a = .b"],
        });

        Assert.True(folders.LinkedMode);
        Assert.Equal(".a = .b", folders.LinkRuleText);

        var captured = folders.CaptureOptions(AppSettings.Default);
        Assert.True(captured.FolderLinkedMode);
        Assert.Equal([".a = .b"], captured.FolderLinkRules);
    }

    [AvaloniaFact]
    public void An_empty_stored_rule_list_keeps_the_built_in_conventions()
    {
        var (folders, _) = Build();

        folders.ApplyDefaults(AppSettings.Default);

        Assert.Contains(".verified", folders.LinkRuleText, StringComparison.Ordinal);
    }

    // ---- Options --------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Exclusions_are_split_on_commas_and_whitespace()
    {
        var (folders, service) = Build();
        folders.ExcludeList = "bin, obj  *.dll;.git";

        await folders.CompareAsync();

        Assert.Equal(["bin", "obj", "*.dll", ".git"], service.Options!.Exclude);
    }

    [AvaloniaFact]
    public async Task An_empty_exclusion_entry_is_dropped()
    {
        // A stray comma would otherwise become a pattern that matches nothing and puzzles the reader.
        var (folders, service) = Build();
        folders.ExcludeList = "bin,,  ,obj";

        await folders.CompareAsync();

        Assert.Equal(["bin", "obj"], service.Options!.Exclude);
    }

    [AvaloniaFact]
    public async Task The_toggles_reach_the_comparison()
    {
        var (folders, service) = Build();
        folders.Recursive = false;
        folders.CompareContents = false;

        await folders.CompareAsync();

        Assert.False(service.Options!.Recursive);
        Assert.False(service.Options.CompareContents);
    }

    [AvaloniaFact]
    public void Settings_are_restored_and_captured()
    {
        var (folders, _) = Build();

        folders.ApplyDefaults(AppSettings.Default with
        {
            FolderShowIdentical = true,
            FolderExclude = ["one", "two"],
        });

        Assert.True(folders.ShowIdentical);
        Assert.Equal("one, two", folders.ExcludeList);

        var captured = folders.CaptureOptions(AppSettings.Default);
        Assert.True(captured.FolderShowIdentical);
        Assert.Equal(["one", "two"], captured.FolderExclude);
    }

    [AvaloniaFact]
    public void An_empty_stored_exclusion_list_keeps_the_defaults()
    {
        // A settings file written before folder comparison existed must not silently turn off the
        // exclusions that make the feature usable.
        var (folders, _) = Build();
        var before = folders.ExcludeList;

        folders.ApplyDefaults(AppSettings.Default);

        Assert.Equal(before, folders.ExcludeList);
        Assert.Contains(".git", folders.ExcludeList, StringComparison.Ordinal);
    }
}
