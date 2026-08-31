namespace Fubar.Diff.Core.Json;

/// <summary>
/// How the Json view should lay a document out when asked to pretty-print it.
///
/// Separate from every other option in the app because it decides something none of them do: what the
/// user LOOKS at. Comparison options decide which values differ, the text-level ones decide which
/// lines look equal - this changes neither, and reformatting a side never changes what the comparison
/// says about it.
/// </summary>
public sealed record JsonFormatOptions
{
    /// <summary>Two spaces, simple containers kept inline - what most JSON in the wild looks like.</summary>
    public static JsonFormatOptions Default { get; } = new();

    /// <summary>How many spaces per level. Ignored when <see cref="UseTabs"/> is on.</summary>
    public int IndentSize { get; init; } = 2;

    /// <summary>Indent with tabs instead of spaces.</summary>
    public bool UseTabs { get; init; }

    /// <summary>
    /// Keep an object or array that contains only scalars on ONE line.
    ///
    /// On by default, and the single setting that most changes how a real document reads. Without it
    /// <c>{"id": 1, "name": "a"}</c> inside an array becomes four lines, and an array of ten such
    /// objects becomes forty - most of them braces. With it, the shape of the data stays visible:
    /// anything genuinely nested still expands, because that is where the structure is.
    /// </summary>
    public bool InlineSimpleContainers { get; init; } = true;

    /// <summary>
    /// Order properties by name rather than as written.
    ///
    /// Off by default: it makes two documents easier to read against each other, and it is also a lie
    /// about what the file contains. Worth having when the two sides come from serializers that
    /// disagree about ordering, which is exactly when reading them side by side is hardest.
    /// </summary>
    public bool SortProperties { get; init; }

    /// <summary>A space after the colon in <c>"name": value</c>. Off gives the more compact form.</summary>
    public bool SpaceAfterColon { get; init; } = true;

    /// <summary>The string one level of indentation is written as.</summary>
    public string IndentUnit => UseTabs ? "\t" : new string(' ', IndentSize < 0 ? 0 : IndentSize);
}
