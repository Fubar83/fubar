namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// A workspace named on the command line: <c>FubarAPIStudio path/to/workspace</c>.
/// </summary>
/// <remarks>
/// <para>The window used to ignore its arguments entirely — it restored the last session and nothing
/// else — so the obvious thing to type, and the thing a file manager does when you open a workspace
/// with this app, silently did nothing. Fubar Diff has taken two paths since its first release; these
/// two are one product family and should not disagree about that.</para>
/// <para>Registered in DI so the shell receives it like any other dependency rather than reading
/// <c>Environment.GetCommandLineArgs()</c> itself, which would make it untestable — the same reason
/// <c>StartupFiles</c> exists on the Diff side.</para>
/// <para>Only the FIRST argument is considered, and only when it is not a flag. Everything with a
/// meaning on the command line is handled before a window exists (see
/// <c>CommandLine.IsHeadless</c>): by the time this runs, an argument is either a path or something
/// nobody meant, and guessing which of several it might be is how a stray argument becomes a tab.</para>
/// </remarks>
public sealed record StartupWorkspace(string? Path)
{
    /// <summary>Nothing on the command line — the app opens the last session and stops there.</summary>
    public static StartupWorkspace None { get; } = new((string?)null);

    public static StartupWorkspace FromArgs(string[] args) =>
        args is [{ Length: > 0 } first, ..] && !first.StartsWith('-')
            ? new StartupWorkspace(first)
            : None;

    /// <summary>True when there is something to open once the last session has been restored.</summary>
    public bool HasPath => !string.IsNullOrWhiteSpace(Path);

    /// <summary>
    /// The workspace directory <paramref name="path"/> refers to: absolute, and the containing
    /// directory when it names the manifest itself.
    /// </summary>
    /// <remarks>
    /// <para>Both spellings have to work. A shell hands over whatever was typed, which is usually
    /// relative and sometimes <c>.</c>; a file manager opening a workspace with this app hands over
    /// <c>fubar.json</c>, because that is the file it knows how to associate — and the app's own Open
    /// dialog picks that same file, since a folder picker cannot show files and there was otherwise no
    /// way to see which directory really was a workspace.</para>
    /// <para>A trailing separator is stripped before the name is read: <c>GetFileName</c> of
    /// <c>C:\ws\</c> is empty, so a path someone tab-completed would never match the manifest test and
    /// the check would look like it worked while doing nothing.</para>
    /// </remarks>
    public static string Directory(string path)
    {
        var full = System.IO.Path.GetFullPath(path);

        // A root IS its separator - trimming C:\ down to C: turns an absolute path into a
        // drive-relative one, which resolves somewhere else entirely.
        if (string.Equals(System.IO.Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase))
        {
            return full;
        }

        var trimmed = full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        return string.Equals(
            System.IO.Path.GetFileName(trimmed),
            Core.Workspaces.IWorkspaceStore.ManifestFileName,
            StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetDirectoryName(trimmed) ?? trimmed
            : trimmed;
    }
}
