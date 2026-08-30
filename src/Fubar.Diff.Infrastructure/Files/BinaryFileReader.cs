using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Files;

namespace Fubar.Diff.Infrastructure.Files;

/// <summary>
/// <see cref="IBinaryFileReader"/> over the local file system.
///
/// Deliberately thin - reading bytes has no decisions in it. The two it does make are the size cap and
/// the same exception type the text reader throws, so a caller handling one path handles both.
/// </summary>
public sealed class BinaryFileReader : IBinaryFileReader
{
    /// <summary>
    /// The same 64 MB ceiling the text reader uses, for a related reason: these bytes are held for as
    /// long as the comparison is open, and both sides are held at once. A hex view of a two-gigabyte
    /// disk image is not a feature anyone is asking this app for.
    /// </summary>
    private const long MaxBytes = 64L * 1024 * 1024;

    public async Task<BinaryDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
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

        return new BinaryDocument(path, bytes, ImageFormatDetector.Detect(bytes));
    }
}
