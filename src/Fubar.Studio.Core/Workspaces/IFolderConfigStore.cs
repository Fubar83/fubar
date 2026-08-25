using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>A folder's <c>_folder.json</c> (inherited headers + auth assignment).</summary>
public interface IFolderConfigStore
{
    /// <summary>Loads a folder's <c>_folder.json</c>, or an empty <see cref="FolderConfig"/> if it has none.</summary>
    Task<FolderConfig> LoadFolderConfigAsync(string folderPath, CancellationToken cancellationToken = default);

    Task SaveFolderConfigAsync(string folderPath, FolderConfig config, CancellationToken cancellationToken = default);
}
