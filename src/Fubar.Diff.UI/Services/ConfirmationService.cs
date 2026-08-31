using System.Collections.Generic;
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
    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel) =>
        await ChooseAsync(title, message, [confirmLabel]).ConfigureAwait(true) == 0;

    public async Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices)
    {
        if (Owner is not { } owner)
        {
            // No window to be modal to. Answering "none of them" is the only safe outcome - a prompt
            // that cannot be shown must never count as agreement to whatever it was going to ask.
            return -1;
        }

        return await new ConfirmWindow(title, message, choices)
            .ShowDialog<int>(owner)
            .ConfigureAwait(true);
    }

    public async Task<string?> AskForTextAsync(string title, string message, string initial = "")
    {
        if (Owner is not { } owner)
        {
            return null;
        }

        return await new PromptWindow(title, message, initial)
            .ShowDialog<string?>(owner)
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
