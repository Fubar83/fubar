using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Fubar.Diff.UI.Services;

/// <summary>
/// <see cref="IFilePickerService"/> over Avalonia's StorageProvider, resolving the owner window from
/// the desktop lifetime so callers do not have to pass one around.
/// </summary>
public sealed class FilePickerService : IFilePickerService
{
    public async Task<string?> PickFileAsync(string title)
    {
        if (MainWindow is not { } window)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title)
    {
        if (MainWindow is not { } window)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (MainWindow is not { } window)
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static Window? MainWindow =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
