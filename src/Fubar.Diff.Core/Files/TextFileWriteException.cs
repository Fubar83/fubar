using System;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// A file could not be written. Like <see cref="TextFileReadException"/>, it carries a message fit to
/// show the user directly, so the UI never has to translate a raw <c>System.IO</c> exception.
/// </summary>
public sealed class TextFileWriteException : Exception
{
    public TextFileWriteException(string path, string reason)
        : base($"Could not save '{path}': {reason}")
    {
        Path = path;
        Reason = reason;
    }

    public TextFileWriteException(string path, string reason, Exception innerException)
        : base($"Could not save '{path}': {reason}", innerException)
    {
        Path = path;
        Reason = reason;
    }

    /// <summary>The file that could not be written.</summary>
    public string Path { get; }

    /// <summary>Why, phrased for a user rather than a log.</summary>
    public string Reason { get; }
}
