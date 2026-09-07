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

            // Loops rather than serving exactly one connection. A browser opens more sockets than it
            // sends requests on - Chrome speculatively pre-connects, and the callback page prompts a
            // /favicon.ico of its own - and accepting one of those as THE redirect ends the sign-in
            // before the redirect arrives, with "the redirect carried neither a code nor an error"
            // for a redirect that was still in flight. Anything without a query string is answered
            // and ignored; only a request that actually carries one is treated as the callback.
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                using var stream = client.GetStream();

                var query = QueryOf(await ReadRequestLineAsync(stream, cancellationToken));

                if (query.Length == 0)
                {
                    await RespondAsync(stream, "Waiting for the provider to redirect…", cancellationToken);

                    continue;
                }

                var result = AuthorizationCodeFlow.ReadCallback(query, request.State);

                // The browser is left showing this, so it has to say what happened - a blank tab after
                // a sign-in is indistinguishable from one that failed.
                await RespondAsync(stream, Describe(result), cancellationToken);

                return result;
            }
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
    ///
    /// <para>Reads until the line ends rather than taking whatever one <c>ReadAsync</c> returned. A
    /// single read is not a line: TCP is a stream, and this particular line carries an authorization
    /// code and a state, both provider-sized - Entra's run to hundreds of characters. A line split
    /// across segments would be truncated mid-code and rejected as a state mismatch, which is the
    /// error that means "somebody forged this redirect".</para>
    /// </summary>
    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();

        // Bounded, because this socket is reachable by anything on the machine: without a cap, a
        // process that connects and streams bytes without a newline would grow this buffer until the
        // app died. 64 KB is far beyond any real request line and far below anything that hurts.
        while (text.Length < 64 * 1024)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                break; // connection closed before a full line - a pre-connect, or a probe
            }

            text.Append(Encoding.ASCII.GetString(buffer, 0, read));

            var end = text.ToString().IndexOf('\r');

            if (end >= 0)
            {
                return text.ToString(0, end);
            }
        }

        return text.ToString();
    }

    private static string QueryOf(string requestLine)
    {
        var parts = requestLine.Split(' ');
        var target = parts.Length > 1 ? parts[1] : "";
        var question = target.IndexOf('?');

        return question < 0 ? "" : target[(question + 1)..];
    }

    private static string Describe(AuthorizationCallback result) =>
        result.Ok
            ? "Signed in. You can close this tab and go back to Fubar API Studio."
            : $"Sign-in failed: {result.Error}. {result.ErrorDescription}";

    private static async Task RespondAsync(NetworkStream stream, string message, CancellationToken cancellationToken)
    {
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
