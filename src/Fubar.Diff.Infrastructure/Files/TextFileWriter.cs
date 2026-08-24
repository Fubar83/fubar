using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Merge;

namespace Fubar.Diff.Infrastructure.Files;

/// <summary>
/// <see cref="ITextFileWriter"/> over the local file system.
///
/// Reconstructs the file with the encoding, BOM and terminator it was READ with, so saving a merge
/// does not also rewrite every line ending or add a byte order mark - changes that would show up as a
/// whole-file diff in the user's version control and bury the edit they actually made.
///
/// Writes via a temporary file and an atomic replace, so an interrupted save cannot leave a truncated
/// file where the user's data used to be.
/// </summary>
public sealed class TextFileWriter : ITextFileWriter
{
    public async Task WriteAsync(
        string path,
        IReadOnlyList<string> lines,
        TextFormat format,
        CancellationToken cancellationToken = default)
    {
        var encoding = ResolveEncoding(format);
        var text = MergedDocument.ToText(lines, format);

        // Same directory as the target: a temp file on another volume cannot be moved atomically.
        var temporaryPath = path + ".fubardiff.tmp";

        try
        {
            await File.WriteAllTextAsync(temporaryPath, text, encoding, cancellationToken).ConfigureAwait(false);

            // File.Move overwrites atomically on both Windows and Unix. File.Replace would preserve
            // ACLs too, but it fails outright when the destination does not exist - which is exactly
            // the Save As to a new path case.
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            TryCleanUp(temporaryPath);
            throw new TextFileWriteException(path, ex.Message, ex);
        }
        catch (OperationCanceledException)
        {
            TryCleanUp(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Maps the recorded format back to an encoding. The BOM flag is honoured separately from the
    /// name, because UTF-8 with and without a BOM share the web name <c>utf-8</c>.
    /// </summary>
    private static Encoding ResolveEncoding(TextFormat format) => format.EncodingName switch
    {
        "utf-16" => new UnicodeEncoding(bigEndian: false, byteOrderMark: format.HasByteOrderMark),
        "utf-16BE" or "unicodeFFFE" => new UnicodeEncoding(bigEndian: true, byteOrderMark: format.HasByteOrderMark),
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: format.HasByteOrderMark),
    };

    private static void TryCleanUp(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the write already failed, and a leftover temp file is not worth masking the
            // real error with a second exception.
        }
    }
}
