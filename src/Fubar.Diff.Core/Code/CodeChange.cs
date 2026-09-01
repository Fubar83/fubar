namespace Fubar.Diff.Core.Code;

/// <summary>
/// What happened to one member between the two files.
///
/// The kinds are ordered by how much they matter, and <see cref="CodeChange.Kind"/> takes the most
/// significant one that applies - a member that was both rewritten and moved is <see cref="Modified"/>,
/// with the move recorded as a flag beside it. That is the same arrangement <c>AlignedLine</c> makes
/// for <c>IsMoved</c> and <c>IsIgnored</c>, and for the same reason: a second fact about a change is
/// not a second kind of change, and adding it as one lands it in every switch over the others.
/// </summary>
public enum CodeChangeKind
{
    /// <summary>Present on the right and not on the left.</summary>
    Added,

    /// <summary>Present on the left and not on the right.</summary>
    Removed,

    /// <summary>Its code changed - different tokens, not merely different spacing.</summary>
    Modified,

    /// <summary>
    /// Its name changed and its code did not, which is how it was matched at all.
    ///
    /// Worth its own kind rather than an add plus a remove: a rename is one decision, it is what a
    /// reviewer needs told, and reporting it as a member vanishing and an unrelated one appearing is
    /// exactly the reading a structural diff exists to prevent.
    /// </summary>
    Renamed,

    /// <summary>
    /// Written differently and compiled identically: reindented, rewrapped, a comment edited, a doc
    /// comment added.
    ///
    /// The most valuable answer in this list, because it is the one nothing else gives. A line differ
    /// cannot tell a reformatted method from a rewritten one - both are a block of red beside a block
    /// of green - and "this file has no functional changes" is a conclusion people currently reach by
    /// reading every line of it.
    /// </summary>
    Cosmetic,

    /// <summary>
    /// The same member, unchanged, somewhere else in the file. Reported only when nothing else about
    /// it changed; otherwise it is the <see cref="CodeChange.IsMoved"/> flag on a louder kind.
    /// </summary>
    Moved,
}

/// <summary>
/// One difference between the structures of two source files.
/// </summary>
/// <param name="Path">Where in the file, e.g. <c>Reporting.Report.Total(int)</c>.</param>
/// <param name="Kind">What happened. See <see cref="CodeChangeKind"/>.</param>
/// <param name="Left">The left-hand node, or null when the member was added.</param>
/// <param name="Right">The right-hand node, or null when the member was removed.</param>
public sealed record CodeChange(
    string Path,
    CodeChangeKind Kind,
    CodeNode? Left,
    CodeNode? Right)
{
    /// <summary>
    /// True when the member also changed position among its siblings.
    ///
    /// A flag rather than a kind, so that a method which was rewritten AND moved still reports as
    /// rewritten - the more important half - while the move stays sayable. Set for
    /// <see cref="CodeChangeKind.Moved"/> too, so a consumer asking only "did this move" gets one
    /// answer rather than two.
    /// </summary>
    public bool IsMoved { get; init; }

    /// <summary>
    /// How deeply nested the member is - 0 for a using or a top-level type, 1 for a member of one.
    ///
    /// Set by the differ, which knows it from its own recursion. NOT derivable from
    /// <see cref="Path"/>, which is the mistake worth pointing at: counting the dots in
    /// <c>System.Collections.Generic</c> says a top-level using is two levels deep, because a
    /// namespace name has dots of its own.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// The type or namespace this member sits in - <c>Reporting.Report</c>. Empty at the top level.
    ///
    /// Carried rather than derived from <see cref="Path"/> for the same reason <see cref="Depth"/> is:
    /// splitting the path at its last dot says a top-level <c>using System.Collections.Generic</c>
    /// lives in <c>System.Collections</c>, which is not a container and does not exist.
    /// </summary>
    public string Container { get; init; } = string.Empty;

    /// <summary>The kind or kinds this node is, one of which is <see cref="Kind"/>. For display.</summary>
    public string Description => (Kind, IsMoved) switch
    {
        (CodeChangeKind.Moved, _) => "moved",
        (CodeChangeKind.Added, _) => "added",
        (CodeChangeKind.Removed, _) => "removed",
        (CodeChangeKind.Modified, true) => "changed and moved",
        (CodeChangeKind.Modified, false) => "changed",
        (CodeChangeKind.Renamed, true) => "renamed and moved",
        (CodeChangeKind.Renamed, false) => "renamed",
        (CodeChangeKind.Cosmetic, true) => "reformatted and moved",
        (CodeChangeKind.Cosmetic, false) => "reformatted",
        _ => Kind.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// True when this changes what the file DOES, as opposed to how it reads.
    ///
    /// A move is not functional in a language where declaration order does not matter, and neither is
    /// reformatting. Everything else is - including a rename, which is a change to the public surface
    /// even when the body is untouched.
    /// </summary>
    public bool IsFunctional => Kind is not (CodeChangeKind.Cosmetic or CodeChangeKind.Moved);

    /// <summary>The kind of node this change is about, whichever side it exists on.</summary>
    public CodeMemberKind MemberKind => (Right ?? Left)?.Kind ?? CodeMemberKind.Other;

    /// <summary>What to show as the name of the thing that changed.</summary>
    public string DisplayName => Kind == CodeChangeKind.Renamed && Left is not null && Right is not null
        ? $"{Left.Name} → {Right.Name}"
        : (Right ?? Left)?.Name ?? Path;

    public override string ToString() => $"{Path} {Description}";
}
