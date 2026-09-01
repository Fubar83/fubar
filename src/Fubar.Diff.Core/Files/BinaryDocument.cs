using System;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// One side of a binary comparison: the file's own bytes, plus what they turned out to be.
///
/// The bytes are kept rather than a hash, because everything the view offers - the first differing
/// offset, the hex around it, the picture itself - needs them. That is also why the text reader's size
/// cap applies here too: this is held in memory for as long as the tab is open.
/// </summary>
/// <param name="Path">The file this came from.</param>
/// <param name="Bytes">Its complete contents.</param>
/// <param name="Format">The image container it announces, or <see cref="ImageFormat.None"/>.</param>
public sealed record BinaryDocument(string Path, ReadOnlyMemory<byte> Bytes, ImageFormat Format)
{
    /// <summary>The file's size in bytes.</summary>
    public int Length => Bytes.Length;

    /// <summary>True when this is something the app can show as a picture rather than as hex.</summary>
    public bool IsImage => Format != ImageFormat.None;
}
