using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Resolves the current <see cref="Avalonia.Controls.Window"/> lazily at call time (rather than
/// holding a reference), so it works regardless of DI construction order relative to
/// <c>MainWindow</c>.
/// </summary>
public sealed class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(string title)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return null;
        }

        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        var result = await window.StorageProvider.OpenFolderPickerAsync(options);
        var folder = result.Count > 0 ? result[0] : null;
        return folder?.TryGetLocalPath();
    }

    public async Task<string?> PickFileAsync(string title, string fileName)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return null;
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(fileName) { Patterns = [fileName] }],
        };
        var result = await window.StorageProvider.OpenFilePickerAsync(options);
        var file = result.Count > 0 ? result[0] : null;
        return file?.TryGetLocalPath();
    }
}
