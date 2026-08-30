using System.Threading;
using System.Threading.Tasks;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// PORT. Reads a file as raw bytes, for content that is not text.
///
/// Separate from <see cref="ITextFileReader"/> rather than a mode of it: they answer different
/// questions and return different things, and one reader with an "as bytes" flag would push the
/// decision onto every caller instead of onto the one place that knows.
/// </summary>
public interface IBinaryFileReader
{
    /// <summary>Reads the file at <paramref name="path"/> as bytes.</summary>
    /// <exception cref="TextFileReadException">The file is missing, unreadable, or over the size limit.</exception>
    Task<BinaryDocument> ReadAsync(string path, CancellationToken cancellationToken = default);
}
