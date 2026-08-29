namespace Fubar.Diff.Core.Merge;

/// <summary>
/// Which of a three-way merge's three documents a row, or a decision, refers to.
///
/// Separate from <see cref="Models.DiffSide"/> rather than an extension of it: that one names the two
/// columns of a comparison, and every switch over it in the codebase is exhaustive on two cases. A
/// third member there would silently fall into a lot of <c>_ =&gt;</c> arms that currently mean "right".
/// </summary>
public enum MergeSide
{
    /// <summary>The common ancestor - what both sides started from.</summary>
    Base,

    /// <summary>One of the two edits. "Theirs", by the convention the two-way view already uses.</summary>
    Left,

    /// <summary>The other edit. "Mine".</summary>
    Right,
}

/// <summary>
/// What happened to one region of the base document, once both edits are taken into account.
///
/// This is the entire point of a three-way merge: with only two documents you can see THAT something
/// differs, but not who changed it, so every difference needs a human. With the ancestor in hand, most
/// differences answer themselves - only one side touched them - and what is left is the genuinely
/// contested set.
/// </summary>
public enum MergeKind
{
    /// <summary>All three agree. Not a region at all - stable context.</summary>
    Unchanged,

    /// <summary>Only the left side changed here, so its version wins with nothing to ask.</summary>
    LeftOnly,

    /// <summary>Only the right side changed here.</summary>
    RightOnly,

    /// <summary>
    /// Both sides changed this region, and to the SAME thing - two people making the same edit, which
    /// happens constantly with cherry-picks and reformatting. Resolvable without asking.
    /// </summary>
    BothSame,

    /// <summary>
    /// Both sides changed this region, differently. The only kind that needs a person, and the only
    /// one navigation stops on by default.
    /// </summary>
    Conflict,
}

/// <summary>What the user has decided about one merge region.</summary>
public enum MergeChoice
{
    /// <summary>
    /// No decision. The merge falls back to what the region's <see cref="MergeKind"/> implies: the
    /// side that changed for a one-sided region, and the BASE text for an unresolved conflict - see
    /// <see cref="ThreeWayMergedDocument"/> for why that is the safe default rather than the useful one.
    /// </summary>
    Unresolved,

    /// <summary>Keep the ancestor's version, discarding both edits.</summary>
    TakeBase,

    /// <summary>Use the left side's version.</summary>
    TakeLeft,

    /// <summary>Use the right side's version.</summary>
    TakeRight,
}
