namespace Fubar.Studio.Core.Workspaces;

/// <summary>
/// What may name a document whose name IS its file name.
/// </summary>
/// <remarks>
/// <para>Cases and batches are both addressed by their file name rather than by anything inside the
/// file - <c>get-order#not-found</c> and <c>@smoke</c> both resolve against a directory listing - so
/// renaming one moves a file, and a name that cannot be a file name cannot be one of these either.</para>
/// <para>One rule in one place because the two were written twice and would drift: the second copy
/// started life as a batch-shaped predicate being called about cases.</para>
/// </remarks>
public static class DocumentName
{
    /// <summary>
    /// Whether <paramref name="name"/> can name a document.
    /// </summary>
    /// <remarks>
    /// Both separators are rejected explicitly rather than left to
    /// <see cref="System.IO.Path.GetInvalidFileNameChars"/>, which on Unix reports only NUL and
    /// <c>/</c> - a name holding a backslash would pass there and produce one file on Windows and
    /// another on Linux out of the same workspace.
    /// </remarks>
    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.Contains('/')
        && !name.Contains('\\')
        && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
}
