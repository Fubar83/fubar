namespace Fubar.Diff.Core.Models;

/// <summary>Which of the two documents in a comparison is meant.</summary>
public enum DiffSide
{
    /// <summary>The left-hand document - by convention the original / theirs.</summary>
    Left,

    /// <summary>The right-hand document - by convention the current / mine, and the merge base.</summary>
    Right,
}
