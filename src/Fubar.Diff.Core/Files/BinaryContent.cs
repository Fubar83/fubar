using System;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// Deciding whether a file is text or not.
///
/// Domain policy rather than a reader detail, which is why it lives here: the text reader needs the
/// answer to refuse a file, and the comparison needs the same answer to offer a binary comparison
/// instead. Two implementations of "is this binary" that disagreed would produce a file that is
/// refused as binary by one path and diffed as text by the other.
/// </summary>
public static class BinaryContent
{
    /// <summary>
    /// How much of the file to inspect. A fixed prefix rather than the whole file: this runs on every
    /// comparison, the signal is overwhelmingly in the header, and a text file that turns binary at
    /// megabyte nine is not a thing that happens.
    /// </summary>
    public const int SniffBytes = 8000;

    /// <summary>
    /// Whether the content looks binary.
    ///
    /// A NUL byte in the first few KB is the classic, cheap signal, and the one git uses - real text
    /// encodings do not produce one. UTF-16 does, on nearly every ASCII character, which is why its
    /// BOM is checked first: without that, every UTF-16 file in the world reads as binary.
    /// </summary>
    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (HasUtf16Preamble(bytes))
        {
            return false;
        }

        var limit = Math.Min(bytes.Length, SniffBytes);

        return bytes[..limit].IndexOf((byte)0) >= 0;
    }

    private static bool HasUtf16Preamble(ReadOnlySpan<byte> b) =>
        b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF));
}
