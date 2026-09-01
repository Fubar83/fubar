using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Working out which file a copy would actually read and which it would write.
///
/// Every mistake this feature could make is a mistake about WHICH FILE, and the copier itself holds no
/// policy - so this is where the whole thing is either safe or not, and it is testable without a disk.
/// </summary>
public class FileCopyPlannerTests
{
    private const string Left = @"C:\left";
    private const string Right = @"D:\right";

    private static FolderEntry File(
        string relativePath,
        FolderEntryStatus status,
        string? leftPath = null,
        string? rightPath = null) =>
        new(relativePath, relativePath, IsDirectory: false, status, 1, 1, [])
        {
            LeftRelativePath = leftPath ?? (status is FolderEntryStatus.RightOnly ? null : relativePath),
            RightRelativePath = rightPath ?? (status is FolderEntryStatus.LeftOnly ? null : relativePath),
        };

    private static FileCopy? Plan(FolderEntry entry, CopyDirection direction) =>
        FileCopyPlanner.Plan(entry, Left, Right, direction);

    [Fact]
    public void A_changed_file_copies_over_the_other_side()
    {
        var copy = Plan(File("a.txt", FolderEntryStatus.Different), CopyDirection.ToRight)!;

        Assert.Equal(Path.Combine(Left, "a.txt"), copy.SourcePath);
        Assert.Equal(Path.Combine(Right, "a.txt"), copy.DestinationPath);
        Assert.True(copy.Overwrites);
    }

    [Fact]
    public void A_left_only_file_copies_into_a_side_that_does_not_have_it()
    {
        var copy = Plan(File("new.txt", FolderEntryStatus.LeftOnly), CopyDirection.ToRight)!;

        Assert.Equal(Path.Combine(Right, "new.txt"), copy.DestinationPath);

        // Nothing is being replaced, which is the single most important thing to say before asking
        // someone to confirm.
        Assert.False(copy.Overwrites);
    }

    [Fact]
    public void A_left_only_file_cannot_be_copied_leftwards()
    {
        // There is no source. Offering it would be offering to copy a file's own absence.
        Assert.Null(Plan(File("new.txt", FolderEntryStatus.LeftOnly), CopyDirection.ToLeft));
    }

    [Fact]
    public void A_right_only_file_cannot_be_copied_rightwards()
    {
        Assert.Null(Plan(File("new.txt", FolderEntryStatus.RightOnly), CopyDirection.ToRight));
    }

    [Fact]
    public void Identical_files_have_nothing_to_copy()
    {
        Assert.Null(Plan(File("a.txt", FolderEntryStatus.Same), CopyDirection.ToRight));
        Assert.Null(Plan(File("a.txt", FolderEntryStatus.Same), CopyDirection.ToLeft));
    }

    [Fact]
    public void A_directory_is_never_copied_as_itself()
    {
        var directory = new FolderEntry("src", "src", IsDirectory: true, FolderEntryStatus.Different, -1, -1, [])
        {
            LeftRelativePath = "src",
            RightRelativePath = "src",
        };

        Assert.Null(Plan(directory, CopyDirection.ToRight));
    }

    [Fact]
    public void The_destination_uses_the_spelling_that_side_already_has()
    {
        // Names pair case-insensitively, so these two rows are one entry. Writing the SOURCE's
        // spelling would leave README.md beside readme.md on a case-sensitive filesystem instead of
        // replacing it - a bug that only appears on someone else's machine.
        var entry = File("README.md", FolderEntryStatus.Different, leftPath: "README.md", rightPath: "readme.md");

        Assert.Equal(Path.Combine(Right, "readme.md"), Plan(entry, CopyDirection.ToRight)!.DestinationPath);
        Assert.Equal(Path.Combine(Left, "README.md"), Plan(entry, CopyDirection.ToLeft)!.DestinationPath);
    }

    [Fact]
    public void Relative_paths_become_real_ones()
    {
        var copy = Plan(File("src/app/main.cs", FolderEntryStatus.Different), CopyDirection.ToRight)!;

        Assert.Equal(Path.Combine(Left, "src", "app", "main.cs"), copy.SourcePath);
        Assert.Equal(Path.Combine(Right, "src", "app", "main.cs"), copy.DestinationPath);
    }

    [Fact]
    public void Accepting_a_snapshot_works_in_one_folder_mode()
    {
        // Linked mode: one root, and the two halves of a pair differ by NAME rather than by folder.
        // Copying right to left is exactly "accept this .received as the new .verified", which is the
        // whole reason snapshot review wanted copying at all.
        var entry = new FolderEntry("Thing.json", "Thing.json", false, FolderEntryStatus.Different, 1, 1, [])
        {
            LeftRelativePath = "Thing.verified.json",
            RightRelativePath = "Thing.received.json",
        };

        var copy = FileCopyPlanner.Plan(entry, @"C:\snaps", @"C:\snaps", CopyDirection.ToLeft)!;

        Assert.Equal(Path.Combine(@"C:\snaps", "Thing.received.json"), copy.SourcePath);
        Assert.Equal(Path.Combine(@"C:\snaps", "Thing.verified.json"), copy.DestinationPath);
        Assert.True(copy.Overwrites);
    }

    [Fact]
    public void A_new_snapshot_with_no_baseline_creates_one()
    {
        var entry = new FolderEntry("Thing.json", "Thing.json", false, FolderEntryStatus.RightOnly, -1, 1, [])
        {
            LeftRelativePath = null,
            RightRelativePath = "Thing.received.json",
        };

        var copy = FileCopyPlanner.Plan(entry, @"C:\snaps", @"C:\snaps", CopyDirection.ToLeft)!;

        // No baseline to take a name from, so the source's own name is used - which is the honest
        // fallback, and the case a user then renames by hand if they meant something else.
        Assert.Equal(Path.Combine(@"C:\snaps", "Thing.received.json"), copy.DestinationPath);
        Assert.False(copy.Overwrites);
    }

    // ---- Whole subtrees ---------------------------------------------------------------------------

    private static FolderEntry Tree() =>
        new("src", "src", IsDirectory: true, FolderEntryStatus.Different, -1, -1,
        [
            File("src/a.txt", FolderEntryStatus.Different),
            File("src/new.txt", FolderEntryStatus.LeftOnly),
            File("src/same.txt", FolderEntryStatus.Same),
            File("src/theirs.txt", FolderEntryStatus.RightOnly),
            new("src/deep", "deep", true, FolderEntryStatus.Different, -1, -1,
            [
                File("src/deep/b.txt", FolderEntryStatus.Different),
            ]),
        ])
        {
            LeftRelativePath = "src",
            RightRelativePath = "src",
        };

    [Fact]
    public void A_folder_means_every_file_under_it()
    {
        var copies = FileCopyPlanner.PlanAll(Tree(), Left, Right, CopyDirection.ToRight);

        // a.txt, new.txt and deep/b.txt. The identical one has nothing to copy and the right-only one
        // has no source on this side.
        Assert.Equal(3, copies.Count);
        Assert.Contains(copies, c => c.DestinationPath.EndsWith("b.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(copies, c => c.SourcePath.EndsWith("same.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(copies, c => c.SourcePath.EndsWith("theirs.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void The_number_that_would_be_REPLACED_is_visible_before_anything_happens()
    {
        // What the confirmation is built on: creating two files and replacing two others are very
        // different acts, and only one of them can lose work.
        var copies = FileCopyPlanner.PlanAll(Tree(), Left, Right, CopyDirection.ToRight);

        Assert.Equal(2, copies.Count(c => c.Overwrites));
    }

    [Fact]
    public void The_other_direction_picks_up_the_other_side_only_files()
    {
        var copies = FileCopyPlanner.PlanAll(Tree(), Left, Right, CopyDirection.ToLeft);

        Assert.Equal(3, copies.Count);
        Assert.Contains(copies, c => c.SourcePath.EndsWith("theirs.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(copies, c => c.SourcePath.EndsWith("new.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void A_folder_of_nothing_but_identical_files_plans_nothing()
    {
        var entry = new FolderEntry("src", "src", true, FolderEntryStatus.Same, -1, -1,
        [
            File("src/a.txt", FolderEntryStatus.Same),
        ]);

        Assert.Empty(FileCopyPlanner.PlanAll(entry, Left, Right, CopyDirection.ToRight));
    }
}
