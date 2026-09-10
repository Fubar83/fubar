namespace Fubar.Studio.Core.Workspaces;

/// <summary>
/// What may name a file in a workspace.
/// </summary>
/// <remarks>
/// <para>Cases and batches are both addressed by their file name rather than by anything inside the
/// file - <c>get-order#not-found</c> and <c>@smoke</c> both resolve against a directory listing - so
/// renaming one moves a file, and a name that cannot be a file name cannot be one of these either.</para>
/// <para>One rule in one place because the two were written twice and would drift: the second copy
/// started life as a batch-shaped predicate being called about cases. The importers and the snapshot
/// store share the same rule through <see cref="Sanitize"/>, since a name they derive from a spec or
/// an environment goes on to be a file in the same workspace.</para>
/// </remarks>
public static class DocumentName
{
    /// <summary>
    /// The characters no workspace file name may contain, on any platform.
    /// </summary>
    /// <remarks>
    /// <para>Spelled out rather than taken from <see cref="System.IO.Path.GetInvalidFileNameChars"/>,
    /// which answers a question about the HOST: on Windows it returns these plus the control
    /// characters, and on Unix only <c>/</c> and NUL. A workspace is a folder of files meant to be
    /// committed and shared, so the rule has to be about the FORMAT - the strictest of the platforms
    /// it can be checked out on - or a case someone names <c>smoke?</c> on Linux becomes a repository
    /// nobody on Windows can check out, and the machine that created it is the one machine where
    /// nothing looks wrong.</para>
    /// <para>This shipped the other way round and CI caught it, which is the only reason it is a
    /// three-line story: the Linux runner accepted names the Windows developer's machine rejected, so
    /// the two agreed on the tests and disagreed about the product.</para>
    /// </remarks>
    private static readonly char[] Invalid = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Names Windows refuses whatever the extension, because they are devices rather than files.
    /// </summary>
    /// <remarks>
    /// <c>CON.json</c> is not a file on Windows, and never will be. Rejecting them everywhere costs a
    /// Linux user the ability to call a case <c>aux</c>, and saves the team the checkout that fails
    /// with an error naming no file anyone recognises.
    /// </remarks>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Whether <paramref name="name"/> can name a document.</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Invalid) >= 0)
        {
            return false;
        }

        // A control character is not typeable by accident, but it IS pasteable - and a name carrying a
        // newline produces a file on Linux that Windows refuses and a shell renders as two lines.
        if (name.Any(char.IsControl))
        {
            return false;
        }

        // Windows silently strips both, so "orders " and "orders" would be one file there and two
        // here - and the second one to be written would overwrite a file the user never named.
        if (name[^1] is '.' or ' ')
        {
            return false;
        }

        // "." and ".." are directory entries, not names. Neither can reach outside the folder on its
        // own (a separator is rejected above), but both would be written as a path that resolves
        // somewhere else entirely.
        return name is not ("." or "..") && !Reserved.Contains(StemOf(name));
    }

    /// <summary>
    /// <paramref name="name"/> with everything that cannot be in a file name replaced by <c>_</c>, or
    /// <paramref name="fallback"/> when nothing usable is left.
    /// </summary>
    /// <remarks>
    /// For names this app DERIVES rather than accepts - a folder from an OpenAPI tag, an environment
    /// name that becomes a snapshot's file name. A person typing a name is told it is not allowed
    /// (<see cref="IsValid"/>); a name arriving from a spec has nobody to tell, so it is made usable
    /// instead of failing an import over a colon in a tag.
    /// </remarks>
    public static string Sanitize(string? name, string fallback = "Untitled")
    {
        var cleaned = new string([.. (name ?? string.Empty)
            .Trim()
            .Select(c => Invalid.Contains(c) || char.IsControl(c) ? '_' : c)])
            .TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or ".."
            ? fallback
            : Reserved.Contains(StemOf(cleaned)) ? cleaned + "_" : cleaned;
    }

    /// <summary>The part a reserved name is recognised by: <c>CON.json</c> is <c>CON</c>.</summary>
    private static string StemOf(string name)
    {
        var dot = name.IndexOf('.');
        return dot < 0 ? name : name[..dot];
    }
}
