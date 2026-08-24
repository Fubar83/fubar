using System;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// PORT. Parses JSON text into an AST that remembers where each value came from.
///
/// A port rather than a direct call to <c>System.Text.Json</c> because that API does not expose
/// per-node line and column, which is precisely what the semantic diff needs to render itself in a
/// text editor.
/// </summary>
public interface IJsonParser
{
    /// <summary>
    /// Parses a complete JSON document.
    /// </summary>
    /// <exception cref="JsonParseException">The text is not valid JSON.</exception>
    JsonAstNode Parse(string text);

    /// <summary>
    /// Parses, returning false instead of throwing when the text is not valid JSON.
    ///
    /// The comparison path uses this: failing to parse is the NORMAL case for a plain text file, and
    /// a broken JSON file is exactly when a diff is most wanted, so it must fall back rather than
    /// fail.
    /// </summary>
    bool TryParse(string text, out JsonAstNode? node, out JsonParseException? error);
}

/// <summary>
/// The text was not valid JSON. Carries a location so the UI can point at the problem rather than
/// just reporting that something, somewhere, is wrong.
/// </summary>
public sealed class JsonParseException : Exception
{
    public JsonParseException(string message, SourceSpan span)
        : base(span.IsKnown ? $"{message} (line {span.StartLine}, column {span.StartColumn})" : message)
    {
        Span = span;
        Reason = message;
    }

    /// <summary>Where the parse failed.</summary>
    public SourceSpan Span { get; }

    /// <summary>The problem, without the position suffix.</summary>
    public string Reason { get; }
}
