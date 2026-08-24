using System.Linq;
using System.Threading.Tasks;
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
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
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
}
