using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Files;

namespace Fubar.Diff.Infrastructure.Files;

/// <summary>
/// <see cref="ITextFileReader"/> over the local file system. Detects the encoding from a BOM (falling
/// back to UTF-8), records the dominant line ending, and refuses files that are plainly binary rather
/// than rendering a screen of replacement characters.
/// </summary>
public sealed class TextFileReader : ITextFileReader
{
    /// <summary>
    /// Files above this size are rejected. A side-by-side view materialises every line as a row, so a
    /// multi-gigabyte file is an out-of-memory crash, not a slow render. 64 MB is far beyond any
    /// source file while still leaving room for large logs and data dumps.
    /// </summary>
    private const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>How much of the file to inspect when deciding whether it is text.</summary>
    private const int SniffBytes = 8000;

    public async Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
        {
            throw new TextFileReadException(path, "the file does not exist.");
        }

        if (info.Length > MaxBytes)
        {
            throw new TextFileReadException(
                path,
                $"it is {info.Length / (1024 * 1024)} MB, larger than the {MaxBytes / (1024 * 1024)} MB limit.");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new TextFileReadException(path, ex.Message, ex);
        }

        if (LooksBinary(bytes))
        {
            throw new TextFileReadException(path, "it appears to be a binary file.");
        }

        var encoding = DetectEncoding(bytes, out var preambleLength);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);

        return new TextDocument(
            path,
            SplitLines(text),
            encoding.WebName,
            DetectLineEnding(text));
    }

    /// <summary>
    /// A NUL byte in the first few KB is the classic, cheap binary signal - real text encodings do not
    /// produce one (UTF-16 would, but its BOM is checked first and it is handled as text).
    /// </summary>
    private static bool LooksBinary(byte[] bytes)
    {
        if (HasUtf16Preamble(bytes))
        {
            return false;
        }

        var limit = bytes.Length < SniffBytes ? bytes.Length : SniffBytes;
        for (var i = 0; i < limit; i++)
        {
            if (bytes[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUtf16Preamble(byte[] b) =>
        b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF));

    private static Encoding DetectEncoding(byte[] b, out int preambleLength)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        {
            preambleLength = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            preambleLength = 2;
            return Encoding.Unicode;
        }

        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            preambleLength = 2;
            return Encoding.BigEndianUnicode;
        }

        // No BOM: assume UTF-8, which is a superset of ASCII and the overwhelming default.
        preambleLength = 0;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static LineEnding DetectLineEnding(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr && crlf > 0)
        {
            return LineEnding.Crlf;
        }

        return cr > lf ? LineEnding.Cr : LineEnding.Lf;
    }

    /// <summary>
    /// Splits on any of the three terminators. A trailing terminator does NOT produce a final empty
    /// line: "a\n" is one line, matching what every editor shows.
    /// </summary>
    private static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        return lines.Length > 1 && lines[^1].Length == 0
            ? lines[..^1]
            : lines;
    }
}
