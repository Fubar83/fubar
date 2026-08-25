namespace Fubar.Diff.Core.Files;

/// <summary>
/// How a file was encoded on disk, captured at read time so a save can reproduce it exactly.
///
/// Every field here exists because it cannot be recovered from the lines alone. Saving without them
/// silently rewrites the file's shape - adding or dropping a BOM, flipping every terminator, or losing
/// the trailing newline - each of which turns a one-line merge into a whole-file diff in version
/// control and buries the edit the user actually made.
/// </summary>
/// <param name="EncodingName">The encoding's web name, e.g. <c>utf-8</c>.</param>
/// <param name="HasByteOrderMark">Whether the file began with a byte order mark.</param>
/// <param name="LineEnding">The dominant line terminator in the source.</param>
/// <param name="EndsWithNewline">
/// Whether the file ended with a terminator. The reader drops the empty string after a final newline
/// (so <c>"a\n"</c> is one line, as every editor shows it), which means this is the only record that
/// it was there. POSIX text files conventionally end with one.
/// </param>
public sealed record TextFormat(
    string EncodingName,
    bool HasByteOrderMark,
    LineEnding LineEnding,
    bool EndsWithNewline = true)
{
    /// <summary>The default for new or in-memory content: UTF-8, no BOM, LF, trailing newline.</summary>
    public static TextFormat Default { get; } = new("utf-8", HasByteOrderMark: false, LineEnding.Lf);
}
