using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Fubar.Studio.UI.Services;

/// <summary>Resolves the current <see cref="Avalonia.Controls.Window"/> lazily at call time, same
/// as <see cref="FolderPickerService"/> - Avalonia's clipboard lives on the <c>TopLevel</c>/Window,
/// not as a static service.</summary>
public sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            && window.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
