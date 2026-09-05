using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

public partial class TokenRequestEditorView : UserControl
{
    public TokenRequestEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens the provider's own console, where the client id this screen asks for is created.
    ///
    /// <para>Code-behind rather than a command because it is a shell operation on a URL the view model
    /// already holds - the same reason <c>LoopbackAuthorizationCodeListener</c> opens the browser
    /// itself. A failure is swallowed for the same reason too: the URL is on screen either way, and a
    /// machine with no registered browser handler should not turn a convenience into an error.</para>
    /// </summary>
    private void OpenConsole_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TokenRequestEditorViewModel { ConsoleUrl: { Length: > 0 } url })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.Exception)
        {
            // Nothing to report: the setup text beside this button names the console it opens.
        }
    }
}
