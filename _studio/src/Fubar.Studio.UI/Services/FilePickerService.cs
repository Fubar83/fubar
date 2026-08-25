using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Fubar.Studio.UI.Services;

/// <summary>Resolves the current <see cref="Avalonia.Controls.Window"/> lazily at call time, same
/// as <see cref="FolderPickerService"/>.</summary>
public sealed class FilePickerService : IFilePickerService
{
    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return null;
        }

        var options = new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedFileName };
        var file = await window.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenFileAsync(string title)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return null;
        }

        var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false };
        var result = await window.StorageProvider.OpenFilePickerAsync(options);
        var file = result.Count > 0 ? result[0] : null;
        return file?.TryGetLocalPath();
    }
}
