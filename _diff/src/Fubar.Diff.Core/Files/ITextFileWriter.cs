using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// PORT. Writes text back to a file, preserving how it was encoded when it was read.
///
/// The <see cref="TextFormat"/> comes from the <see cref="TextDocument"/> that was loaded, so a save
/// round-trips the file's own conventions instead of silently rewriting CRLF to LF or dropping a BOM.
/// </summary>
public interface ITextFileWriter
{
    /// <summary>
    /// Writes <paramref name="lines"/> to <paramref name="path"/> in the given
    /// <paramref name="format"/>.
    /// </summary>
    /// <exception cref="TextFileWriteException">The file could not be written.</exception>
    Task WriteAsync(
        string path,
        IReadOnlyList<string> lines,
        TextFormat format,
        CancellationToken cancellationToken = default);
}
