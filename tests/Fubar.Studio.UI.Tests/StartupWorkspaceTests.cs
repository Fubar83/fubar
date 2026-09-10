using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// A workspace named on the command line.
///
/// <para>The window used to ignore its arguments completely - it restored the last session and
/// nothing else - so <c>FubarAPIStudio path/to/workspace</c>, and a file manager opening a workspace
/// with this app, both did nothing at all. Fubar Diff has taken two file paths since its first
/// release; the two apps should not disagree about whether arguments mean anything.</para>
/// </summary>
public class StartupWorkspaceTests
{
    [Fact]
    public void No_arguments_means_the_last_session_and_nothing_else()
    {
        Assert.False(StartupWorkspace.FromArgs([]).HasPath);
        Assert.Null(StartupWorkspace.FromArgs([]).Path);
    }

    [Fact]
    public void The_first_argument_is_the_workspace()
    {
        Assert.Equal("C:/work/orders", StartupWorkspace.FromArgs(["C:/work/orders"]).Path);
    }

    [Fact]
    public void A_flag_is_not_a_path()
    {
        // Everything with a meaning on the command line is handled before a window exists
        // (CommandLine.IsHeadless), so anything still here is a path or a mistake - and opening a tab
        // called "--verbose" is the wrong way to report a mistake.
        Assert.False(StartupWorkspace.FromArgs(["--something"]).HasPath);
        Assert.False(StartupWorkspace.FromArgs(["-x", "C:/work/orders"]).HasPath);
    }

    [Fact]
    public void Only_the_first_one_counts()
    {
        // One window, one workspace to bring forward. Opening every path on the line would make a
        // stray argument into a tab, and there is no second thing here that could be meant.
        Assert.Equal("C:/first", StartupWorkspace.FromArgs(["C:/first", "C:/second"]).Path);
    }

    // ---- Which directory a path means -----------------------------------------------------------

    [Fact]
    public void A_relative_path_is_resolved_against_the_working_directory()
    {
        // What a shell hands over is usually relative, and often just ".".
        Assert.Equal(Directory.GetCurrentDirectory(), StartupWorkspace.Directory("."));
    }

    [Fact]
    public void Naming_the_manifest_names_its_workspace()
    {
        // A file manager opening a workspace with this app passes fubar.json, because that is the file
        // it knows how to associate - and the app's own Open dialog picks that same file.
        var root = Path.Combine(Path.GetTempPath(), "ws");

        Assert.Equal(
            Path.GetFullPath(root),
            StartupWorkspace.Directory(Path.Combine(root, "fubar.json")));
    }

    [Fact]
    public void A_trailing_separator_does_not_hide_the_manifest()
    {
        // Path.GetFileName of "C:\ws\" is empty, so a tab-completed path would never match the
        // manifest test - the check would look like it worked while doing nothing.
        var root = Path.Combine(Path.GetTempPath(), "ws");

        Assert.Equal(Path.GetFullPath(root), StartupWorkspace.Directory(root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void A_root_keeps_its_separator()
    {
        // Trimming C:\ down to C: turns an absolute path into a drive-relative one, which resolves
        // somewhere else entirely.
        var root = Path.GetPathRoot(Directory.GetCurrentDirectory())!;

        Assert.Equal(root, StartupWorkspace.Directory(root));
    }
}
