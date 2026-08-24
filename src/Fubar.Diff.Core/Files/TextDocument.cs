using System.Collections.Generic;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// A loaded text document: its lines plus the details needed to describe it in the UI and to write it
/// back in the same shape it arrived.
/// </summary>
/// <param name="Path">Where it came from. Empty for in-memory content (tests, pasted text).</param>
/// <param name="Lines">The content, split on line boundaries, with no terminators.</param>
/// <param name="Format">Encoding, BOM, and line-ending details, preserved for saving.</param>
public sealed record TextDocument(
    string Path,
    IReadOnlyList<string> Lines,
    TextFormat Format)
{
    /// <summary>An empty document - what a pane shows before a file is chosen.</summary>
    public static TextDocument Empty { get; } = new(string.Empty, [], TextFormat.Default);

    /// <summary>The file name alone, for window titles and tab labels.</summary>
    public string DisplayName =>
        string.IsNullOrEmpty(Path) ? "(untitled)" : System.IO.Path.GetFileName(Path);
}

/// <summary>The line terminator convention a document uses.</summary>
public enum LineEnding
{
    /// <summary>Unix: <c>\n</c>.</summary>
    Lf,

    /// <summary>Windows: <c>\r\n</c>.</summary>
    Crlf,

    /// <summary>Classic Mac: <c>\r</c>.</summary>
    Cr,
}
