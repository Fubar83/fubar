using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests.Workspaces;

/// <summary>Exercises WorkspaceService against a real temp directory - it's a thin wrapper over
/// the file system, so faking that out would just re-implement it twice.</summary>
public class WorkspaceServiceTests : IDisposable
{
    private readonly string _root;
    private readonly WorkspaceService _sut = new();

    public WorkspaceServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fubar-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void IsWorkspaceRoot_False_WhenNoAppJson()
    {
        Assert.False(_sut.IsWorkspaceRoot(_root));
    }

    [Fact]
    public async Task IsWorkspaceRoot_True_AfterSavingAppManifest()
    {
        await _sut.SaveAppManifestAsync(_root, new AppManifest { Name = "Test" });

        Assert.True(_sut.IsWorkspaceRoot(_root));
    }

    [Fact]
    public async Task LoadWorkspaceAsync_ReadsManifestAndAuth()
    {
        await _sut.SaveAppManifestAsync(_root, new AppManifest { Id = "abc", Name = "Test Workspace" });
        await File.WriteAllTextAsync(Path.Combine(_root, "auth.json"), """{ "type": "bearer", "token": "" }""");

        var workspace = await _sut.LoadWorkspaceAsync(_root);

        Assert.Equal("abc", workspace.WorkspaceId);
        Assert.Equal("Test Workspace", workspace.Manifest.Name);
        Assert.NotNull(workspace.DefaultAuth);
        Assert.Equal(AuthType.Bearer, workspace.DefaultAuth.Type);
    }

    [Fact]
    public async Task LoadWorkspaceAsync_DefaultAuthIsNull_WhenAuthJsonMissing()
    {
        await _sut.SaveAppManifestAsync(_root, new AppManifest { Name = "Test" });

        var workspace = await _sut.LoadWorkspaceAsync(_root);

        Assert.Null(workspace.DefaultAuth);
    }

    [Fact]
    public void BuildCollectionsTree_Empty_WhenCollectionsDirMissing()
    {
        Assert.Empty(_sut.BuildCollectionsTree(_root));
    }

    [Fact]
    public void BuildCollectionsTree_ReflectsNestedFoldersAndRequests_DirectoriesBeforeFiles()
    {
        var usersDir = Directory.CreateDirectory(Path.Combine(_root, "collections", "users")).FullName;
        File.WriteAllText(Path.Combine(usersDir, "get-user.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "collections", "root-request.json"), "{}");

        var tree = _sut.BuildCollectionsTree(_root);

        Assert.Equal(2, tree.Count);
        Assert.True(tree[0].IsDirectory);
        Assert.Equal("users", tree[0].Name);
        Assert.Single(tree[0].Children);
        Assert.Equal("get-user.json", tree[0].Children[0].Name);
        Assert.False(tree[1].IsDirectory);
        Assert.Equal("root-request.json", tree[1].Name);
    }

    [Fact]
    public void CreateRequest_WritesValidRequestJson_AndReturnsItsPath()
    {
        var collectionsDir = Path.Combine(_root, "collections");

        var path = _sut.CreateRequest(collectionsDir, "Get User");

        Assert.True(File.Exists(path));
        Assert.EndsWith("Get User.json", path);
    }

    [Fact]
    public void CreateRequest_DisambiguatesName_WhenFileAlreadyExists()
    {
        var collectionsDir = Path.Combine(_root, "collections");
        var first = _sut.CreateRequest(collectionsDir, "New Request");

        var second = _sut.CreateRequest(collectionsDir, "New Request");

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void CreateFolder_CreatesDirectory_AndReturnsItsPath()
    {
        var collectionsDir = Path.Combine(_root, "collections");

        var path = _sut.CreateFolder(collectionsDir, "Users");

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void RenamePath_MovesFile_PreservingExtension()
    {
        var collectionsDir = Path.Combine(_root, "collections");
        var original = _sut.CreateRequest(collectionsDir, "Old Name");

        var renamed = _sut.RenamePath(original, "New Name");

        Assert.False(File.Exists(original));
        Assert.True(File.Exists(renamed));
        Assert.EndsWith("New Name.json", renamed);
    }

    [Fact]
    public void DuplicatePath_CopiesFile_WithCopySuffix()
    {
        var collectionsDir = Path.Combine(_root, "collections");
        var original = _sut.CreateRequest(collectionsDir, "Original");

        var duplicate = _sut.DuplicatePath(original);

        Assert.True(File.Exists(original));
        Assert.True(File.Exists(duplicate));
        Assert.Contains("(copy)", duplicate);
    }

    [Fact]
    public void DeletePath_RemovesFile()
    {
        var collectionsDir = Path.Combine(_root, "collections");
        var path = _sut.CreateRequest(collectionsDir, "ToDelete");

        _sut.DeletePath(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeletePath_RemovesDirectoryRecursively()
    {
        var collectionsDir = Path.Combine(_root, "collections");
        var folder = _sut.CreateFolder(collectionsDir, "ToDelete");
        _sut.CreateRequest(folder, "Inner");

        _sut.DeletePath(folder);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task GetInheritanceChainAsync_CollectsHeaders_RootMostFirst()
    {
        var usersDir = Directory.CreateDirectory(Path.Combine(_root, "collections", "users")).FullName;
        var requestPath = _sut.CreateRequest(usersDir, "Get User");

        await _sut.SaveFolderConfigAsync(Path.Combine(_root, "collections"), new FolderConfig
        {
            Headers = [new KeyValueItem { Key = "X-Tenant-Id", Value = "{{tenantId}}" }],
        });
        await _sut.SaveFolderConfigAsync(usersDir, new FolderConfig
        {
            Headers = [new KeyValueItem { Key = "X-Scope", Value = "users" }],
            AuthProfileId = "admin-profile",
        });

        var chain = await _sut.GetInheritanceChainAsync(_root, requestPath);

        Assert.Equal(2, chain.Headers.Count);
        Assert.Equal("X-Tenant-Id", chain.Headers[0].Item.Key);
        Assert.Equal("X-Scope", chain.Headers[1].Item.Key);
        Assert.Equal("admin-profile", chain.AuthProfileId);
    }

    [Fact]
    public async Task GetInheritanceChainAsync_NearestAncestorAuthProfileWins()
    {
        var usersDir = Directory.CreateDirectory(Path.Combine(_root, "collections", "users")).FullName;
        var requestPath = _sut.CreateRequest(usersDir, "Get User");

        await _sut.SaveFolderConfigAsync(Path.Combine(_root, "collections"), new FolderConfig { AuthProfileId = "root-profile" });
        await _sut.SaveFolderConfigAsync(usersDir, new FolderConfig { AuthProfileId = "users-profile" });

        var chain = await _sut.GetInheritanceChainAsync(_root, requestPath);

        Assert.Equal("users-profile", chain.AuthProfileId);
    }

    [Fact]
    public async Task GetInheritanceChainAsync_Empty_WhenNoFolderConfigsExist()
    {
        var usersDir = Directory.CreateDirectory(Path.Combine(_root, "collections", "users")).FullName;
        var requestPath = _sut.CreateRequest(usersDir, "Get User");

        var chain = await _sut.GetInheritanceChainAsync(_root, requestPath);

        Assert.Empty(chain.Headers);
        Assert.Null(chain.AuthProfileId);
    }

    [Fact]
    public async Task SaveEnvironmentAsync_ThenLoadEnvironmentsAsync_RoundTrips()
    {
        await _sut.SaveEnvironmentAsync(_root, new WorkspaceEnvironment
        {
            Name = "Staging",
            Variables = [new AppVariable { Key = "baseUrl", Value = "https://staging.example.com" }],
        });

        var environments = await _sut.LoadEnvironmentsAsync(_root);

        Assert.Single(environments);
        Assert.Equal("Staging", environments[0].Name);
        Assert.Equal("baseUrl", environments[0].Variables[0].Key);
    }

    [Fact]
    public async Task SaveEnvironmentAsync_OmitsValue_ForSecretAndSessionVariables_AndRoundTripsKind()
    {
        await _sut.SaveEnvironmentAsync(_root, new WorkspaceEnvironment
        {
            Name = "Staging",
            Variables =
            [
                new AppVariable { Key = "apiKey", Value = null, Kind = VariableKind.Secret },
                new AppVariable { Key = "runId", Value = null, Kind = VariableKind.Session },
            ],
        });

        var environments = await _sut.LoadEnvironmentsAsync(_root);

        var apiKey = environments[0].Variables.Single(v => v.Key == "apiKey");
        var runId = environments[0].Variables.Single(v => v.Key == "runId");
        Assert.Equal(VariableKind.Secret, apiKey.Kind);
        Assert.Null(apiKey.Value);
        Assert.Equal(VariableKind.Session, runId.Kind);
        Assert.Null(runId.Value);
    }

    [Fact]
    public async Task SaveAuthProfilesAsync_ThenLoadAuthProfilesAsync_RoundTrips()
    {
        await _sut.SaveAuthProfilesAsync(_root, [new AuthProfile { Name = "Admin", Config = new AuthConfig { Type = AuthType.Bearer, Token = "abc" } }]);

        var profiles = await _sut.LoadAuthProfilesAsync(_root);

        Assert.Single(profiles);
        Assert.Equal("Admin", profiles[0].Name);
        Assert.Equal(AuthType.Bearer, profiles[0].Config.Type);
    }
}
