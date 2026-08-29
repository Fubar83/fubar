using System.Collections.Generic;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Core.Comparison;

/// <summary>How the two documents should be compared.</summary>
public enum ComparisonMode
{
    /// <summary>
    /// Use semantic comparison when both files parse as JSON, otherwise plain text. The default: it
    /// gives the better answer where it can, and never fails because of it.
    /// </summary>
    Auto,

    /// <summary>Always compare as plain text, even for JSON.</summary>
    Text,

    /// <summary>
    /// Always compare as JSON. Still falls back to text when a file does not parse - a broken file is
    /// exactly when a diff is most wanted.
    /// </summary>
    Json,
}

/// <summary>
/// How strictly two documents should be compared. These are normalisation rules applied before the
/// diff runs, so they change which lines are considered equal - not how the result is displayed.
/// </summary>
public sealed record ComparisonOptions
{
    /// <summary>Strict, byte-for-byte line comparison.</summary>
    public static ComparisonOptions Default { get; } = new();

    /// <summary>Treat lines that differ only in leading/trailing whitespace as equal.</summary>
    public bool IgnoreWhitespace { get; init; }

    /// <summary>Treat lines that differ only in letter case as equal.</summary>
    public bool IgnoreCase { get; init; }

    /// <summary>
    /// Compare structure rather than formatting: XML is re-serialised with consistent indentation
    /// before diffing, so a difference in formatting alone produces no changes. Falls back to plain
    /// text when the content does not parse.
    ///
    /// This is the TEXT-level approximation. For JSON, <see cref="Mode"/> does the same job properly -
    /// by parsing - and this option is redundant there.
    /// </summary>
    public bool NormalizeStructure { get; init; }

    /// <summary>
    /// Compare text in Unicode normal form C, so sequences that render identically compare equal.
    ///
    /// The case this exists for: <c>é</c> is either U+00E9 or <c>e</c> followed by U+0301, and both
    /// draw the same glyph. macOS decomposes where Windows and Linux compose, so the same edit made on
    /// two machines can produce files that differ in every accented word and look identical in every
    /// editor. Off by default - it IS a real difference in the bytes, and a tool whose job is showing
    /// what changed should not hide one until asked.
    /// </summary>
    public bool NormalizeUnicode { get; init; }

    /// <summary>
    /// Regular expressions whose matches are blanked out before two lines are compared - see
    /// <see cref="LinePatternMask"/>.
    ///
    /// For the text that changes on every run and that nobody can act on: a build timestamp, a
    /// generated GUID, a version stamp in a header. Masking the MATCH rather than the whole line is
    /// deliberate, so a real change elsewhere on the same line is still reported.
    /// </summary>
    public IReadOnlyList<string> IgnoredLinePatterns { get; init; } = [];

    /// <summary>Text or semantic comparison. See <see cref="ComparisonMode"/>.</summary>
    public ComparisonMode Mode { get; init; } = ComparisonMode.Auto;

    /// <summary>Settings for the semantic JSON pass, used when it runs.</summary>
    public JsonComparisonOptions Json { get; init; } = JsonComparisonOptions.Default;

    /// <summary>
    /// Settings that apply when the files are source code in a language the scanner knows. Inert for
    /// everything else - the language comes from the file extension, so a pair the scanner cannot read
    /// simply never consults these.
    /// </summary>
    public CodeComparisonOptions Code { get; init; } = CodeComparisonOptions.Default;
}
