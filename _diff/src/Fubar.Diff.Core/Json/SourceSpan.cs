namespace Fubar.Diff.Core.Json;

/// <summary>
/// Where a node sits in the original text, as 1-based line and column.
///
/// This is what lets a semantic difference be shown in a text editor: the differ works on the parsed
/// tree, but the user is looking at lines, and the span is the bridge between them. Without it a
/// semantic diff could only be rendered as its own tree view.
/// </summary>
/// <param name="StartLine">1-based line the node starts on.</param>
/// <param name="StartColumn">1-based column the node starts at.</param>
/// <param name="EndLine">1-based line the node ends on, inclusive.</param>
/// <param name="EndColumn">1-based column just past the node's last character.</param>
public readonly record struct SourceSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    /// <summary>An unknown or not-applicable location.</summary>
    public static SourceSpan None { get; } = new(0, 0, 0, 0);

    /// <summary>True when this points at real text rather than being <see cref="None"/>.</summary>
    public bool IsKnown => StartLine > 0;

    /// <summary>
    /// How many lines the node covers. A scalar is normally 1; an object or array spans its braces.
    /// </summary>
    public int LineCount => IsKnown ? EndLine - StartLine + 1 : 0;

    /// <summary>A span covering both of these and everything between them.</summary>
    public SourceSpan Union(SourceSpan other)
    {
        if (!IsKnown)
        {
            return other;
        }

        if (!other.IsKnown)
        {
            return this;
        }

        var (startLine, startColumn) = Earlier(StartLine, StartColumn, other.StartLine, other.StartColumn);
        var (endLine, endColumn) = Later(EndLine, EndColumn, other.EndLine, other.EndColumn);

        return new SourceSpan(startLine, startColumn, endLine, endColumn);
    }

    private static (int Line, int Column) Earlier(int aLine, int aColumn, int bLine, int bColumn) =>
        aLine < bLine || (aLine == bLine && aColumn <= bColumn) ? (aLine, aColumn) : (bLine, bColumn);

    private static (int Line, int Column) Later(int aLine, int aColumn, int bLine, int bColumn) =>
        aLine > bLine || (aLine == bLine && aColumn >= bColumn) ? (aLine, aColumn) : (bLine, bColumn);

    public override string ToString() =>
        IsKnown ? $"{StartLine}:{StartColumn}-{EndLine}:{EndColumn}" : "(unknown)";
}
