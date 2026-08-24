namespace Fubar.Diff.Core.Models;

/// <summary>
/// A contiguous run of changed rows, used for "next/previous change" navigation. Indices refer to
/// positions in <see cref="DiffResult.Lines"/>, so a viewer can scroll straight to one.
/// </summary>
/// <param name="StartIndex">Index of the first changed row, inclusive.</param>
/// <param name="EndIndex">Index of the last changed row, inclusive.</param>
public sealed record DiffHunk(int StartIndex, int EndIndex)
{
    /// <summary>Number of rows in the hunk.</summary>
    public int Length => EndIndex - StartIndex + 1;
}
