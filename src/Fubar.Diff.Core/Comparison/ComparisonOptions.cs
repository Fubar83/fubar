namespace Fubar.Diff.Core.Comparison;

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
    /// Compare structure rather than formatting: JSON and XML are re-serialised with consistent
    /// indentation before diffing, so a difference in formatting alone produces no changes. Property
    /// order is preserved, so reordering keys IS still reported. Falls back to plain text when the
    /// content does not parse.
    /// </summary>
    public bool NormalizeStructure { get; init; }
}
