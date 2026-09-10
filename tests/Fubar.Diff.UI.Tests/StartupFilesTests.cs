using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// Command-line parsing. Worth its own tests because it is the one piece of the app a user can get
/// wrong from OUTSIDE it - and because <c>git mergetool</c> passes its arguments positionally, so an
/// off-by-one here silently merges the wrong pair of files against the wrong ancestor and looks
/// entirely plausible while doing it.
/// </summary>
public class StartupFilesTests
{
    [Fact]
    public void No_arguments_opens_empty()
    {
        var files = StartupFiles.FromArgs([]);

        Assert.False(files.HasBoth);
        Assert.False(files.IsMerge);
    }

    [Fact]
    public void One_argument_fills_the_left_side_only()
    {
        var files = StartupFiles.FromArgs(["a.txt"]);

        Assert.Equal("a.txt", files.Left);
        Assert.Null(files.Right);
        Assert.False(files.HasBoth);
    }

    [Fact]
    public void Two_arguments_are_a_comparison()
    {
        var files = StartupFiles.FromArgs(["a.txt", "b.txt"]);

        Assert.Equal("a.txt", files.Left);
        Assert.Equal("b.txt", files.Right);
        Assert.True(files.HasBoth);
        Assert.False(files.IsMerge);
    }

    [Fact]
    public void Extra_arguments_are_ignored_rather_than_refused()
    {
        // Refusing to start over a stray argument would be worse than opening the two that were meant.
        var files = StartupFiles.FromArgs(["a.txt", "b.txt", "c.txt"]);

        Assert.Equal("a.txt", files.Left);
        Assert.Equal("b.txt", files.Right);
        Assert.False(files.IsMerge);
    }

    [Fact]
    public void The_merge_flag_takes_base_local_and_remote_in_git_order()
    {
        // git mergetool passes $BASE $LOCAL $REMOTE. LOCAL is "mine" and has to land on the RIGHT, to
        // match the two-way window's convention that the right-hand side is the one being merged into.
        var files = StartupFiles.FromArgs(["--merge", "base.cs", "mine.cs", "theirs.cs"]);

        Assert.True(files.IsMerge);
        Assert.Equal("base.cs", files.Base);
        Assert.Equal("mine.cs", files.Right);
        Assert.Equal("theirs.cs", files.Left);
    }

    [Theory]
    [InlineData("--merge")]
    [InlineData("-m")]
    [InlineData("/merge")]
    public void Every_spelling_of_the_merge_flag_works(string flag)
    {
        var files = StartupFiles.FromArgs([flag, "b.cs", "l.cs", "r.cs"]);

        Assert.True(files.IsMerge);
    }

    [Theory]
    [InlineData("--merge")]
    [InlineData("--merge", "base.cs")]
    [InlineData("--merge", "base.cs", "mine.cs")]
    public void An_incomplete_merge_opens_empty_rather_than_guessing(params string[] args)
    {
        // Two of the three files is not a merge, and picking which one is missing would be a guess
        // about someone's working tree.
        var files = StartupFiles.FromArgs(args);

        Assert.False(files.IsMerge);
        Assert.False(files.HasBoth);
    }

    [Fact]
    public void A_merge_beyond_three_files_ignores_the_rest()
    {
        var files = StartupFiles.FromArgs(["--merge", "b.cs", "l.cs", "r.cs", "merged.cs"]);

        Assert.True(files.IsMerge);
        Assert.Equal("b.cs", files.Base);
    }

    [Fact]
    public void A_file_that_merely_starts_with_a_dash_is_still_a_file()
    {
        // Only the merge flag is a flag. Everything else is positional, so a path is never swallowed.
        var files = StartupFiles.FromArgs(["-weird.txt", "b.txt"]);

        Assert.Equal("-weird.txt", files.Left);
        Assert.False(files.IsMerge);
    }

    // ---- Two directories -------------------------------------------------------------------------

    /// <summary>Stands in for the disk: every named path is a directory, nothing else is.</summary>
    private static Func<string, bool> Directories(params string[] paths) =>
        p => paths.Contains(p, StringComparer.Ordinal);

    [Fact]
    public void Two_directories_are_a_folder_comparison()
    {
        // "FubarDiff old new" over two checkouts used to fall through to the FILE comparison and
        // report "the file does not exist" about a directory that plainly did.
        var files = StartupFiles.FromArgs(["/src/old", "/src/new"]);

        Assert.True(files.IsFolderComparison(Directories("/src/old", "/src/new")));
    }

    [Fact]
    public void Two_files_are_not()
    {
        var files = StartupFiles.FromArgs(["a.cs", "b.cs"]);

        Assert.False(files.IsFolderComparison(Directories()));
    }

    [Fact]
    public void One_of_each_is_not_a_folder_comparison_either()
    {
        // Pairing a directory with a file is a mistake, not a mode - and it is one the file
        // comparison now names properly ("it is a folder, not a file") rather than claiming the
        // directory does not exist.
        var files = StartupFiles.FromArgs(["/src/old", "b.cs"]);

        Assert.False(files.IsFolderComparison(Directories("/src/old")));
    }

    [Fact]
    public void A_merge_is_never_a_folder_comparison()
    {
        // git mergetool names three files by a settled convention; nothing about it is a folder, and
        // a repository that happened to have directories at those paths must not silently become one.
        var files = StartupFiles.FromArgs(["--merge", "/b", "/l", "/r"]);

        Assert.True(files.IsMerge);
        Assert.False(files.IsFolderComparison(Directories("/b", "/l", "/r")));
    }

    [Fact]
    public void One_path_on_its_own_is_not_a_folder_comparison()
    {
        Assert.False(StartupFiles.FromArgs(["/src/old"]).IsFolderComparison(Directories("/src/old")));
    }
}
