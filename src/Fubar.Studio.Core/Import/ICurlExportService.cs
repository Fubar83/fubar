using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Import;

/// <summary>
/// Which shell the command is going to be pasted into.
/// </summary>
/// <remarks>
/// <para>A curl command is not shell-independent, and the differences are not cosmetic. The POSIX form
/// this app emitted for every platform fails outright in PowerShell: <c>\</c> is not a line
/// continuation there (a backtick is), so the parser stops at the first one with "Missing expression
/// after unary operator '--'" and nothing is sent.</para>
/// <para>Which matters most for the app's own platform - a Windows user pasting into their own
/// terminal got a broken command, while the same command worked for everyone on macOS and Linux, and
/// in Git Bash and WSL on Windows.</para>
/// </remarks>
public enum CurlShell
{
    /// <summary>bash, zsh, Git Bash, WSL. Multi-line with <c>\</c> continuations, and an apostrophe
    /// inside a single-quoted argument written the POSIX way, <c>'\''</c>.</summary>
    Posix,

    /// <summary>
    /// PowerShell 7 - and Windows PowerShell 5.1 for everything but a body containing double quotes.
    /// </summary>
    /// <remarks>
    /// <para>Three differences from POSIX, each load-bearing. It is one LINE, because PowerShell
    /// continues with a backtick rather than a backslash. An apostrophe inside a single-quoted string
    /// is doubled (<c>''</c>) rather than escaped POSIX-style. And it calls <c>curl.exe</c> rather than
    /// <c>curl</c>: in Windows PowerShell 5.1 <c>curl</c> is an ALIAS for <c>Invoke-WebRequest</c>,
    /// which understands none of <c>-X</c>, <c>-H</c> or <c>--data</c> and fails on the first of them
    /// whatever the quoting. Naming the executable costs nothing in PowerShell 7, where the alias is
    /// gone.</para>
    /// <para><b>Windows PowerShell 5.1 strips double quotes from a native command's arguments</b>, so a
    /// JSON body arrives as <c>{sku:A}</c> there. That is not something this can escape around, and it
    /// was measured rather than assumed: backslash-escaping the inner quotes fixes 5.1 and breaks 7,
    /// where the backslashes then arrive literally. 5.1 is correct for the URL, the headers and any
    /// body without double quotes. PowerShell 7 is correct for everything, and is what <c>pwsh</c>
    /// is.</para>
    /// </remarks>
    PowerShell,
}

/// <summary>Renders a <see cref="RequestModel"/> as a runnable <c>curl</c> command - the mirror of
/// <see cref="ICurlImportService"/>. <paramref name="resolve"/> substitutes <c>{{variable}}</c> tokens
/// (URL, header values, body) so the emitted command is ready to paste into a shell.</summary>
public interface ICurlExportService
{
    /// <param name="request">The request to render, with its variables about to be resolved.</param>
    /// <param name="resolve">Substitutes <c>{{variable}}</c> tokens.</param>
    /// <param name="shell">
    /// The shell the result is meant for. Defaults to <see cref="CurlShell.Posix"/>, which is what
    /// every existing caller wants and what a curl command looks like in every example anyone has read.
    /// </param>
    string ToCurl(RequestModel request, Func<string?, string> resolve, CurlShell shell = CurlShell.Posix);
}
