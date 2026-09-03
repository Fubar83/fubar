using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Fubar.Studio.UI.Cli;

/// <summary>
/// Gets a Windows GUI application talking to the console that launched it.
///
/// A Windows executable is built either as a console app or as a windowed one, and this is a windowed
/// one - which means that when it is run from a shell, its standard output goes nowhere at all.
/// Writing to Console.Out silently succeeds and the user sees nothing. Attaching to the parent
/// process's console is the standard fix, and the only alternative is shipping a second executable
/// purely to print text.
///
/// Everywhere else there is nothing to do: on macOS and Linux the process already has whatever
/// streams it was given.
/// </summary>
internal static class ParentConsole
{
    private const int AttachParentProcess = -1;

    // DllImport rather than the newer LibraryImport: that one generates its marshalling code and so
    // requires the project to allow unsafe blocks, which is a lot to switch on repo-wide for one
    // function taking an int and returning a bool.
    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    /// <summary>
    /// Attaches, and rebinds the streams so anything already cached inside Console starts going to the
    /// right place.
    ///
    /// Failure is ignored on purpose: there may be no parent console (launched from Explorer, or
    /// redirected to a file), and in that case output either goes to the redirection that IS set up or
    /// goes nowhere - neither of which is a reason to refuse to run the comparison.
    /// </summary>
    public static void Attach()
    {
        if (!OperatingSystem.IsWindows() || !AttachConsole(AttachParentProcess))
        {
            return;
        }

        var standardOutput = new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var standardError = new System.IO.StreamWriter(Console.OpenStandardError()) { AutoFlush = true };

        Console.SetOut(standardOutput);
        Console.SetError(standardError);
    }
}
