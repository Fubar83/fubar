using System;
using System.Collections.Generic;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// A loaded text document: its lines plus the details needed to describe it in the UI and to write it
/// back unchanged if it is only partly edited.
/// </summary>
/// <param name="Path">Where it came from. Empty for in-memory content (tests, pasted text).</param>
/// <param name="Lines">The content, split on line boundaries, with no terminators.</param>
/// <param name="EncodingName">Detected encoding's web name, e.g. <c>utf-8</c>.</param>
/// <param name="LineEnding">The dominant line ending in the source.</param>
public sealed record TextDocument(
    string Path,
    IReadOnlyList<string> Lines,
    string EncodingName,
    LineEnding LineEnding)
{
    /// <summary>An empty document - what a pane shows before a file is chosen.</summary>
    public static TextDocument Empty { get; } = new(string.Empty, [], "utf-8", LineEnding.Lf);

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
