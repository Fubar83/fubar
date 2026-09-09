using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>
/// Moving a request, an endpoint or a folder into another workspace.
///
/// <para>The other half of the scratch pad: something tried out with no filing decisions has to be
/// able to become a real request later, or "try it here first" is a dead end. A move, not a copy -
/// the point is that it stops being where it was.</para>
/// </summary>
public class MovePathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fubar-move-" + Guid.NewGuid().ToString("n"));
    private readonly WorkspaceService _sut = new();

    private string From => Path.Combine(_root, "from");

    private string To => Path.Combine(_root, "to");

    public MovePathTests()
    {
        Directory.CreateDirectory(From);
        Directory.CreateDirectory(To);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string File_(string name, string content = "{}")
    {
        var path = Path.Combine(From, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_request_moves_and_stops_being_where_it_was()
    {
        var path = File_("Get user.json", """{"name":"Get user"}""");

        var moved = _sut.MovePath(path, To);

        Assert.False(File.Exists(path));
        Assert.Equal(Path.Combine(To, "Get user.json"), moved);
        Assert.Contains("Get user", File.ReadAllText(moved), StringComparison.Ordinal);
    }

    /// <summary>An endpoint is a directory, and everything under it goes too - its cases, its
    /// snapshots, its own batches.</summary>
    [Fact]
    public void An_endpoint_moves_with_everything_under_it()
    {
        var endpoint = Path.Combine(From, "get-order");
        Directory.CreateDirectory(Path.Combine(endpoint, "cases"));
        File.WriteAllText(Path.Combine(endpoint, "endpoint.json"), "{}");
        File.WriteAllText(Path.Combine(endpoint, "cases", "default.json"), "{}");

        var moved = _sut.MovePath(endpoint, To);

        Assert.False(Directory.Exists(endpoint));
        Assert.True(File.Exists(Path.Combine(moved, "cases", "default.json")));
    }

    /// <summary>Refused rather than overwritten: the destination copy is somebody's work too.</summary>
    [Fact]
    public void A_name_already_taken_at_the_destination_is_refused()
    {
        var path = File_("Get user.json");
        File.WriteAllText(Path.Combine(To, "Get user.json"), "{}");

        var thrown = Assert.Throws<IOException>(() => _sut.MovePath(path, To));

        Assert.Contains("already exists", thrown.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// A directory cannot be moved inside its own subtree.
    /// </summary>
    /// <remarks>
    /// Not a move anyone meant, and <see cref="Directory.Move"/> does not always refuse it - on some
    /// file systems it succeeds and takes the contents with it.
    /// </remarks>
    [Fact]
    public void A_directory_cannot_be_moved_inside_itself()
    {
        var folder = Path.Combine(From, "orders");
        Directory.CreateDirectory(Path.Combine(folder, "inner"));

        Assert.Throws<IOException>(() => _sut.MovePath(folder, Path.Combine(folder, "inner")));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Moving_something_that_is_not_there_says_so()
    {
        Assert.Throws<FileNotFoundException>(
            () => _sut.MovePath(Path.Combine(From, "gone.json"), To));
    }

    /// <summary>A destination that does not exist yet is made - a workspace whose collections/ was
    /// never created is still somewhere a request can go.</summary>
    [Fact]
    public void A_destination_that_does_not_exist_yet_is_created()
    {
        var path = File_("Get user.json");
        var fresh = Path.Combine(_root, "fresh", "collections");

        var moved = _sut.MovePath(path, fresh);

        Assert.True(File.Exists(moved));
    }

    [Fact]
    public void Moving_somewhere_it_already_is_changes_nothing()
    {
        var path = File_("Get user.json");

        Assert.Equal(path, _sut.MovePath(path, From));
        Assert.True(File.Exists(path));
    }
}
