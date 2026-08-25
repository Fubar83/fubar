using System.Threading;
using System.Threading.Tasks;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// PORT. Reads a text file, dealing with encoding detection and line-ending normalisation. Core never
/// touches System.IO directly - that is what makes the comparison services testable without a disk.
/// </summary>
public interface ITextFileReader
{
    /// <summary>
    /// Reads the file at <paramref name="path"/> as text.
    /// </summary>
    /// <exception cref="TextFileReadException">
    /// The file is missing, unreadable, or not text (see <see cref="TextFileReadException"/>).
    /// </exception>
    Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default);
}
