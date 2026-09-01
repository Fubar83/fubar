using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;

namespace Fubar.Diff.UI.Services;

/// <summary>
/// <see cref="IClipboardService"/> over the active window's clipboard, resolved from the desktop
/// lifetime so callers need not pass a window around.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (ActiveWindow?.Clipboard is not { } clipboard)
        {
            return;
        }

        // Avalonia 12 replaced SetTextAsync with a data-transfer model: a clipboard payload is now a
        // set of typed items rather than a string, which is what lets one copy carry text and files at
        // once. For plain text that is one item.
        using var payload = new DataTransfer();
        payload.Add(DataTransferItem.CreateText(text));

        await clipboard.SetDataAsync(payload).ConfigureAwait(true);
    }

    /// <summary>
    /// The focused window, falling back to the main one.
    ///
    /// The fallback matters: a patch can be copied from a comparison tab, and the merge and folder
    /// windows are separate top-levels - taking the main window unconditionally would copy through a
    /// window the user is not looking at, which on some platforms is a clipboard that does not stick.
    /// </summary>
    private static Window? ActiveWindow =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow
            : null;
}
