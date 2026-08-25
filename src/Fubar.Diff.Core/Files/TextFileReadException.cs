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
}
