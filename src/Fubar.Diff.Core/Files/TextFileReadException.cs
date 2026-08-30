using System;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// A file could not be read as text. Carries a message fit to show the user directly, so the UI does
/// not have to translate raw <c>System.IO</c> exceptions (which the domain never sees).
/// </summary>
public sealed class TextFileReadException : Exception
{
    public TextFileReadException(string path, string reason)
        : base($"Could not read '{path}': {reason}")
    {
        Path = path;
        Reason = reason;
    }

    public TextFileReadException(string path, string reason, Exception innerException)
        : base($"Could not read '{path}': {reason}", innerException)
    {
        Path = path;
        Reason = reason;
    }

    /// <summary>The file that could not be read.</summary>
    public string Path { get; }

    /// <summary>Why, phrased for a user rather than a log.</summary>
    public string Reason { get; }

    /// <summary>
    /// True when the file was rejected specifically for NOT BEING TEXT, rather than for being missing,
    /// locked or too large.
    ///
    /// A flag rather than a caller matching on <see cref="Reason"/>: that string exists to be shown to
    /// a person and will be reworded, and a comparison that silently stops offering binary files
    /// because someone improved the wording is exactly the kind of break nothing would catch.
    /// </summary>
    public bool IsBinary { get; init; }
}
