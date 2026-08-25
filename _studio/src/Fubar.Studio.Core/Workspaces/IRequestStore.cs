using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>Request documents and the <c>collections/</c> file tree: load/save requests plus the
/// create/duplicate/rename/delete file operations over requests and folders.</summary>
public interface IRequestStore
{
    Task<RequestModel> LoadRequestAsync(string requestFilePath, CancellationToken cancellationToken = default);

    Task SaveRequestAsync(string requestFilePath, RequestModel request, CancellationToken cancellationToken = default);

    /// <summary>Scans <c>{rootPath}/collections</c> and returns its direct children as a tree. Empty if
    /// the directory doesn't exist yet.</summary>
    IReadOnlyList<WorkspaceTreeNode> BuildCollectionsTree(string rootPath);

    /// <summary>Creates a blank request.json named after <paramref name="requestName"/> and returns its full path.</summary>
    string CreateRequest(string parentDirectory, string requestName);

    /// <summary>Creates a subfolder named <paramref name="folderName"/> and returns its full path.</summary>
    string CreateFolder(string parentDirectory, string folderName);

    /// <summary>Copies the file or directory at <paramref name="path"/> alongside itself and returns the new path.</summary>
    string DuplicatePath(string path);

    /// <summary>Renames the file or directory at <paramref name="path"/> in place and returns the new path.</summary>
    string RenamePath(string path, string newName);

    /// <summary>Deletes the file, or recursively deletes the directory, at <paramref name="path"/>.</summary>
    void DeletePath(string path);
}
