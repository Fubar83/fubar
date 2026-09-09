using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>
/// Reserving a name without occupying it, and renaming what a name points at.
///
/// <para>A case and a batch are both addressed by their FILE name - <c>get-order#not-found</c> and
/// <c>@smoke</c> resolve against a directory listing - so a draft has to hold the path it will take,
/// and changing the name has to move the file. Otherwise the two names disagree and the document
/// cannot be selected by the name it displays.</para>
/// </summary>
public class DraftPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fubar-drafts-" + Guid.NewGuid().ToString("n"));
    private readonly FileEndpointStore _endpoints = new();
    private readonly FileBatchStore _batches = new();

    private string Endpoint => Path.Combine(_root, "collections", "orders", "get-order");

    public DraftPathTests() => Directory.CreateDirectory(Endpoint);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    // ---- Proposing -------------------------------------------------------------------------------

    /// <summary>The point of it: "New case" must not leave a file behind when you change your mind.</summary>
    [Fact]
    public void Proposing_a_case_writes_nothing()
    {
        var path = _endpoints.ProposeCasePath(Endpoint, "new-case");

        Assert.EndsWith("new-case.json", path);
        Assert.False(File.Exists(path));
        Assert.Empty(_endpoints.ListCases(Endpoint));
    }

    [Fact]
    public void Proposing_a_batch_writes_nothing()
    {
        var path = _batches.ProposeBatchPath(Endpoint, "new-batch");

        Assert.EndsWith("new-batch.json", path);
        Assert.False(File.Exists(path));
        Assert.Empty(_batches.ListBatches(Endpoint));
    }

    /// <summary>A proposal steps around what is already there, so drafting twice in a row does not
    /// hand both drafts the same file to save into.</summary>
    [Fact]
    public void A_proposal_avoids_a_name_that_is_taken()
    {
        _endpoints.CreateCase(Endpoint, "new-case");

        Assert.EndsWith("new-case 2.json", _endpoints.ProposeCasePath(Endpoint, "new-case"));
    }

    // ---- Renaming --------------------------------------------------------------------------------

    [Fact]
    public void Renaming_a_case_moves_the_file()
    {
        var path = _endpoints.CreateCase(Endpoint, "new-case");

        var moved = _endpoints.RenameCase(path, "not-found");

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(moved));
        Assert.Equal(["not-found"], _endpoints.ListCases(Endpoint).Select(c => c.Name));
    }

    [Fact]
    public void Renaming_a_case_onto_an_existing_one_is_refused()
    {
        var first = _endpoints.CreateCase(Endpoint, "created");
        _endpoints.CreateCase(Endpoint, "missing");

        Assert.Throws<IOException>(() => _endpoints.RenameCase(first, "missing"));
        Assert.True(File.Exists(first));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escaped")]
    [InlineData("nested/name")]
    [InlineData("nested\name")]
    public void A_case_name_that_is_not_a_file_name_is_refused(string name)
    {
        var path = _endpoints.CreateCase(Endpoint, "created");

        Assert.Throws<ArgumentException>(() => _endpoints.RenameCase(path, name));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_case_takes_its_own_capitalisation()
    {
        var path = _endpoints.CreateCase(Endpoint, "created");

        _endpoints.RenameCase(path, "Created");

        Assert.Equal("Created", _endpoints.ListCases(Endpoint).Single().Name);
    }

    /// <summary>Saving a draft under a new name writes the new file and leaves nothing at the
    /// reserved one - which is what the tree needs in order to retire the draft row.</summary>
    [Fact]
    public async Task Saving_a_drafted_case_under_a_new_name_leaves_the_proposal_free()
    {
        var proposed = _endpoints.ProposeCasePath(Endpoint, "new-case");

        await _endpoints.SaveCaseAsync(proposed, new EndpointCase { Name = "new-case" });
        var moved = _endpoints.RenameCase(proposed, "not-found");

        Assert.False(File.Exists(proposed));
        Assert.EndsWith("not-found.json", moved);
    }
}
