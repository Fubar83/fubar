namespace Fubar.Studio.Core.Running;

/// <summary>What kind of thing a selector names.</summary>
public enum RunSelectorKind
{
    /// <summary>The whole workspace.</summary>
    Everything,

    /// <summary>A folder, an endpoint, a request, or one case of an endpoint.</summary>
    Path,

    /// <summary>A batch, by name.</summary>
    Batch,
}

/// <summary>
/// One command-line selector, parsed.
/// </summary>
/// <remarks>
/// <para>The grammar is deliberately tiny, and every form of it is something a person would type
/// without looking it up:</para>
/// <code>
///                            the whole workspace
/// orders                     a folder, depth-first
/// orders/get-order           an endpoint, all its cases
/// orders/get-order#default   one case
/// @smoke                     a batch
/// </code>
/// <para><c>#</c> and <c>@</c> rather than more path segments, because <c>orders/get-order/default</c>
/// would be indistinguishable from a folder called <c>default</c>, and a batch named the same as a
/// folder would be ambiguous with no way for the user to say which they meant.</para>
/// </remarks>
public sealed record RunSelector(RunSelectorKind Kind, string? Path = null, string? Case = null)
{
    public static readonly RunSelector Everything = new(RunSelectorKind.Everything);

    /// <summary>The batch's name, for <see cref="RunSelectorKind.Batch"/>.</summary>
    public string? BatchName => Kind == RunSelectorKind.Batch ? Path : null;

    /// <summary>
    /// Parses a selector, or throws <see cref="FormatException"/> with something worth reading.
    /// </summary>
    /// <remarks>
    /// Blank means everything, because that is what <c>fubar run</c> with no argument has always
    /// meant. Every other malformed form is refused rather than guessed at: a selector that quietly
    /// widened to the whole workspace would send every request someone had in the file.
    /// </remarks>
    public static RunSelector Parse(string? text)
    {
        var trimmed = text?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed == ".")
        {
            return Everything;
        }

        if (trimmed[0] == '@')
        {
            var name = trimmed[1..].Trim();

            return name.Length == 0
                ? throw new FormatException("\"@\" names no batch. Write @<name>, e.g. @smoke.")
                : new RunSelector(RunSelectorKind.Batch, name);
        }

        var hash = trimmed.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
        {
            return new RunSelector(RunSelectorKind.Path, Normalize(trimmed));
        }

        var path = Normalize(trimmed[..hash]);
        var selectedCase = trimmed[(hash + 1)..].Trim();

        if (path.Length == 0)
        {
            throw new FormatException($"\"{trimmed}\" names a case but no endpoint.");
        }

        if (selectedCase.Length == 0)
        {
            throw new FormatException($"\"{trimmed}\" ends in \"#\" and names no case.");
        }

        return new RunSelector(RunSelectorKind.Path, path, selectedCase);
    }

    /// <summary>Backslashes accepted, because this is Windows and half the paths pasted in come from
    /// Explorer. Stored with forward slashes, which is what the format uses everywhere else.</summary>
    private static string Normalize(string path) =>
        path.Replace('\\', '/').Trim().Trim('/');

    public override string ToString() => Kind switch
    {
        RunSelectorKind.Everything => "the whole workspace",
        RunSelectorKind.Batch => $"@{Path}",
        _ => Case is null ? Path! : $"{Path}#{Case}",
    };
}
