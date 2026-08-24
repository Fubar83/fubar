namespace Fubar.Diff.Core.Models;

/// <summary>What happened to a single line when going from the left document to the right one.</summary>
public enum ChangeKind
{
    /// <summary>Present and identical on both sides.</summary>
    Unchanged,

    /// <summary>Present only on the right - an addition.</summary>
    Inserted,

    /// <summary>Present only on the left - a deletion.</summary>
    Deleted,

    /// <summary>Present on both sides but with different content.</summary>
    Modified,

    /// <summary>
    /// No line on this side at all. Side-by-side views need a placeholder opposite an insertion or
    /// deletion so the two panes stay vertically aligned; this is that placeholder.
    /// </summary>
    Filler,
}
