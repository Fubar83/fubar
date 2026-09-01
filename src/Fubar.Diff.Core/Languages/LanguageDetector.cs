namespace Fubar.Diff.Core.Languages;

/// <summary>
/// Works out which language a document is written in, from its file extension alone.
///
/// Extension only, on purpose: content sniffing for programming languages is guesswork that fails on
/// exactly the short files where a wrong guess is most visible, and the cost of being wrong here is not
/// "no highlighting" but "the comparison silently applied the wrong rules about what a comment is".
/// A file with no extension gets <see cref="SourceLanguage.None"/> and is compared as plain text,
/// which is always a defensible answer.
/// </summary>
public static class LanguageDetector
{
    /// <summary>
    /// The language for a file path, or <see cref="SourceLanguage.None"/> for an empty path, an unknown
    /// extension, or in-memory content with a label rather than a path.
    /// </summary>
    public static SourceLanguage FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return SourceLanguage.None;
        }

        return FromExtension(System.IO.Path.GetExtension(path));
    }

    /// <summary>The language for a file extension, with or without the leading dot.</summary>
    public static SourceLanguage FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return SourceLanguage.None;
        }

        var ext = extension.StartsWith('.') ? extension[1..] : extension;

        return ext.ToLowerInvariant() switch
        {
            "cs" or "csx" => SourceLanguage.CSharp,
            "js" or "jsx" or "mjs" or "cjs" => SourceLanguage.JavaScript,
            "ts" or "tsx" or "mts" or "cts" => SourceLanguage.TypeScript,
            "java" => SourceLanguage.Java,
            "go" => SourceLanguage.Go,
            "py" or "pyi" or "pyw" => SourceLanguage.Python,

            // .h is ambiguous between C and C++ and always has been. It resolves to C because the
            // scanning rules are identical either way - the only thing the choice affects is the name
            // shown in Settings, and "C" is the less wrong guess for a bare header.
            "c" or "h" => SourceLanguage.C,
            "cpp" or "cc" or "cxx" or "hpp" or "hh" or "hxx" or "ipp" => SourceLanguage.Cpp,

            _ => SourceLanguage.None,
        };
    }

    /// <summary>
    /// The language for a PAIR of documents, which is what a comparison actually needs.
    ///
    /// Both sides normally agree. When they do not - comparing a <c>.js</c> against its <c>.ts</c>
    /// rewrite is a real thing people do - the two are close enough lexically that either answer scans
    /// both correctly, so the left side wins simply to be deterministic. When only ONE side is a known
    /// language (a <c>.cs</c> against a <c>.txt</c> copy of it), that side decides: scanning both with
    /// C# rules is strictly better than scanning neither.
    /// </summary>
    public static SourceLanguage ForPair(string? leftPath, string? rightPath)
    {
        var left = FromPath(leftPath);

        return left != SourceLanguage.None ? left : FromPath(rightPath);
    }
}
