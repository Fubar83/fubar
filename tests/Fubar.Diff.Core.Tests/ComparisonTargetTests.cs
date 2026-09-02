using Fubar.Diff.Core.Files;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// What a pair of chosen paths means, which is the rule behind the open dialog's Compare button.
///
/// Worth having in Core rather than in the window: the same question is asked twice - once to decide
/// whether the button is enabled, and again to decide what to open when it is pressed - and two
/// answers that could disagree is exactly how a button ends up enabled for something that then fails.
/// </summary>
public class ComparisonTargetTests
{
    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        "left.cs", "right.cs",
    };

    private static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        "before", "after", "snapshots",
    };

    private static PathKind Kind(string? path) =>
        ComparisonTargets.Classify(path, Files.Contains, Folders.Contains);

    private static ComparisonTarget Resolve(string? left, string? right) =>
        ComparisonTargets.Resolve(Kind(left), Kind(right));

    // ---- Classifying one path --------------------------------------------------------------------

    [Theory]
    [InlineData(null, PathKind.Empty)]
    [InlineData("", PathKind.Empty)]
    [InlineData("   ", PathKind.Empty)]
    [InlineData("left.cs", PathKind.File)]
    [InlineData("before", PathKind.Folder)]
    [InlineData("nowhere.cs", PathKind.Missing)]
    public void A_path_is_classified_by_what_is_actually_there(string? path, PathKind expected)
    {
        Assert.Equal(expected, Kind(path));
    }

    [Fact]
    public void Surrounding_whitespace_does_not_stop_a_path_being_recognised()
    {
        // Paths arrive from a text box and from drops, and both bring spaces.
        Assert.Equal(PathKind.File, Kind("  left.cs  "));
    }

    [Fact]
    public void A_folder_is_checked_for_first()
    {
        // A directory named like a file - dist.bak, a macOS bundle - must not be taken for one.
        Assert.Equal(PathKind.Folder, ComparisonTargets.Classify(
            "bundle.app", _ => true, path => path == "bundle.app"));
    }

    // ---- What a pair means -----------------------------------------------------------------------

    [Fact]
    public void Two_files_open_a_comparison()
    {
        var target = Resolve("left.cs", "right.cs");

        Assert.Equal(ComparisonTargetKind.Files, target.Kind);
        Assert.True(target.CanCompare);
        Assert.False(target.IsFolders);
        Assert.Null(target.Problem);
    }

    [Fact]
    public void Two_folders_open_the_folder_window()
    {
        var target = Resolve("before", "after");

        Assert.Equal(ComparisonTargetKind.Folders, target.Kind);
        Assert.True(target.IsFolders);
    }

    [Fact]
    public void One_folder_on_its_own_pairs_its_own_contents()
    {
        // What dropping a single folder should do: snapshot review, .received against .verified,
        // rather than waiting for a second folder that may not exist.
        Assert.Equal(ComparisonTargetKind.LinkedFolder, Resolve("snapshots", null).Kind);
        Assert.Equal(ComparisonTargetKind.LinkedFolder, Resolve(null, "snapshots").Kind);
        Assert.True(Resolve("snapshots", "").IsFolders);
    }

    [Fact]
    public void A_file_against_a_folder_is_refused_with_a_reason()
    {
        var target = Resolve("left.cs", "before");

        Assert.Equal(ComparisonTargetKind.Invalid, target.Kind);
        Assert.False(target.CanCompare);
        Assert.Contains("cannot be compared against a folder", target.Problem);
    }

    [Fact]
    public void One_file_asks_for_the_second_rather_than_complaining()
    {
        // An unfinished dialog has not done anything wrong, so it gets a prompt, not an error.
        var target = Resolve("left.cs", null);

        Assert.Equal(ComparisonTargetKind.Incomplete, target.Kind);
        Assert.False(target.CanCompare);
        Assert.Contains("second file", target.Problem);
    }

    [Fact]
    public void An_empty_dialog_says_what_to_do()
    {
        var target = Resolve(null, null);

        Assert.Equal(ComparisonTargetKind.Incomplete, target.Kind);
        Assert.Contains("two files", target.Problem);
    }

    [Fact]
    public void A_path_that_does_not_exist_names_the_side_it_is_on()
    {
        // The one problem the user can fix immediately - so it is reported ahead of everything else,
        // and it says which box to look at.
        Assert.Contains("left", Resolve("nowhere.cs", "right.cs").Problem);
        Assert.Contains("right", Resolve("left.cs", "nowhere.cs").Problem);
        Assert.Equal(ComparisonTargetKind.Invalid, Resolve("nowhere.cs", null).Kind);
    }

    // ---- Symmetry --------------------------------------------------------------------------------

    [Theory]
    [InlineData("left.cs", "right.cs")]
    [InlineData("before", "after")]
    [InlineData("left.cs", "before")]
    [InlineData("snapshots", null)]
    [InlineData("nowhere.cs", "right.cs")]
    public void Swapping_the_sides_never_changes_WHETHER_they_can_be_compared(string? left, string? right)
    {
        // The dialog has a swap button. A rule that only worked one way round would make pressing it
        // change the answer, which is the sort of thing nobody thinks to test until it happens.
        Assert.Equal(Resolve(left, right).CanCompare, Resolve(right, left).CanCompare);
        Assert.Equal(Resolve(left, right).Kind, Resolve(right, left).Kind);
    }
}
