namespace Fubar.Diff.Core.Merge;

/// <summary>What the user has decided about one hunk.</summary>
public enum HunkResolution
{
    /// <summary>No decision yet - the merge keeps whatever the base side says.</summary>
    Unresolved,

    /// <summary>Use the left side's version of this hunk.</summary>
    TakeLeft,

    /// <summary>Use the right side's version of this hunk.</summary>
    TakeRight,
}
