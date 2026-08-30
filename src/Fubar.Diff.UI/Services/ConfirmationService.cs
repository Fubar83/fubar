using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Fubar.Diff.UI.Views;

namespace Fubar.Diff.UI.Services;

/// <summary>
/// <see cref="IConfirmationService"/> over a modal <see cref="ConfirmWindow"/>.
///
/// Owned by the ACTIVE window rather than the main one: the question that needs asking is usually
/// about something in the folder window, and a modal parented to a window behind it is a dialog the
/// user cannot find.
/// </summary>
public sealed class ConfirmationService : IConfirmationService
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        if (Owner is not { } owner)
        {
            // No window to be modal to. Refusing is the only safe answer - a confirmation that cannot
            // be shown must not silently count as a yes.
            return false;
        }

        return await new ConfirmWindow(title, message, confirmLabel)
            .ShowDialog<bool>(owner)
            .ConfigureAwait(true);
    }

    private static Window? Owner
    {
        get
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return null;
            }

            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        }
    }
}
