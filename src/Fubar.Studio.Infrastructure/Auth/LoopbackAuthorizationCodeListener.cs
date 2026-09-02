using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Fubar.Studio.Core.Auth;

namespace Fubar.Studio.Infrastructure.Auth;

/// <summary>
/// ADAPTER. Opens the system browser and catches the provider's redirect on a loopback port.
///
/// A raw <see cref="TcpListener"/> rather than <c>HttpListener</c>, deliberately: HttpListener needs a
/// URL ACL on Windows, which means an elevation prompt the first time - an unacceptable thing to
/// spring on someone in the middle of signing in. Only one request is ever served, and only its
/// request line is read, so a socket and a single read are genuinely enough.
/// </summary>
public sealed class LoopbackAuthorizationCodeListener : IAuthorizationCodeListener
{
    public int ReservePort()
    {
        // Port 0 asks the OS for a free one. There is an unavoidable race between releasing it here
        // and binding it in ListenAsync - nothing can hold a port across a registration the user must
        // perform in a browser - but the window is small and the alternative is asking them to pick a
        // number and hope.
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    public async Task<AuthorizationCallback> ListenAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var port = new Uri(request.RedirectUri).Port;
        var listener = new TcpListener(IPAddress.Loopback, port);

        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            return new AuthorizationCallback(
                null,
                "port_unavailable",
                $"Could not listen on {request.RedirectUri}: {ex.Message}");
        }

        try
        {
            OpenBrowser(request.AuthorizeUrl);

            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            using var stream = client.GetStream();

            var requestLine = await ReadRequestLineAsync(stream, cancellationToken);
            var result = AuthorizationCodeFlow.ReadCallback(QueryOf(requestLine), request.State);

            // The browser is left showing this, so it has to say what happened - a blank tab after a
            // sign-in is indistinguishable from one that failed.
            await RespondAsync(stream, result, cancellationToken);

            return result;
        }
        catch (OperationCanceledException)
        {
            // The ordinary ending when someone closes the tab or gives up, not an error to report as
            // one.
            return new AuthorizationCallback(null, "cancelled", "The sign-in was cancelled.");
        }
        catch (IOException ex)
        {
            return new AuthorizationCallback(null, "redirect_failed", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Reads only the request line - <c>GET /callback?code=… HTTP/1.1</c>. The headers say nothing
    /// this needs, and reading to the end of them is more code and one more way to hang.
    /// </summary>
    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer, cancellationToken);
        var text = Encoding.ASCII.GetString(buffer, 0, read);
        var end = text.IndexOf('\r');

        return end < 0 ? text : text[..end];
    }

    private static string QueryOf(string requestLine)
    {
        var parts = requestLine.Split(' ');
        var target = parts.Length > 1 ? parts[1] : "";
        var question = target.IndexOf('?');

        return question < 0 ? "" : target[(question + 1)..];
    }

    private static async Task RespondAsync(NetworkStream stream, AuthorizationCallback result, CancellationToken cancellationToken)
    {
        var message = result.Ok
            ? "Signed in. You can close this tab and go back to Fubar API Studio."
            : $"Sign-in failed: {result.Error}. {result.ErrorDescription}";

        var body = $"<!doctype html><meta charset=\"utf-8\"><title>Fubar API Studio</title>"
            + $"<body style=\"font-family:system-ui;padding:3rem\"><p>{WebUtility.HtmlEncode(message)}</p>";

        var response = "HTTP/1.1 200 OK\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
            + "Connection: close\r\n\r\n"
            + body;

        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Hands the URL to the OS to open in whatever the user's default browser is.
    ///
    /// The SYSTEM browser rather than an embedded one, which is RFC 8252's whole recommendation: it
    /// already holds the user's session and their password manager, and an embedded webview asking for
    /// corporate credentials is indistinguishable from a phishing page.
    /// </summary>
    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // A machine with no registered browser handler still gets a working flow: the URL is on
            // screen in the editor and can be opened by hand. Failing the whole sign-in here would be
            // a worse answer than a manual copy.
        }
    }
}
