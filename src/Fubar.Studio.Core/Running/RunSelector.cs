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
/// @smoke                     a batch of the workspace's own
/// orders/get-order@happy     a batch belonging to that endpoint
/// </code>
/// <para><c>#</c> and <c>@</c> rather than more path segments, because <c>orders/get-order/default</c>
/// would be indistinguishable from a folder called <c>default</c>, and a batch named the same as a
/// folder would be ambiguous with no way for the user to say which they meant.</para>
/// <para>The qualified form exists because batches now have two homes and a name is only unique
/// within one: the workspace's <c>batches/</c> holds the occasions that cut across the tree, and an
/// endpoint's holds the ways of running that endpoint. Two endpoints may each have a <c>happy</c>,
/// and neither of them is <c>@happy</c>.</para>
/// </remarks>
public sealed record RunSelector(
    RunSelectorKind Kind, string? Path = null, string? Case = null, string? Batch = null)
{
    public static readonly RunSelector Everything = new(RunSelectorKind.Everything);

    /// <summary>The batch's name, for <see cref="RunSelectorKind.Batch"/>.</summary>
    public string? BatchName => Kind == RunSelectorKind.Batch ? Batch ?? Path : null;

    /// <summary>The endpoint whose batch this is, or null for one of the workspace's own. Null is not
    /// "unknown" - it is the workspace, which is a different owner rather than a missing one.</summary>
    public string? BatchOwnerPath => Kind == RunSelectorKind.Batch && Batch is not null ? Path : null;

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

        // Checked before '#', so orders/get-order@happy is a batch rather than a path with an odd
        // name. The two never combine: a batch names its own steps, so a case on top of it would be
        // asking for one step of something that already said which steps it runs.
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            var name = trimmed[(at + 1)..].Trim();
            var owner = Normalize(trimmed[..at]);

            if (name.Length == 0)
            {
                throw new FormatException(
                    owner.Length == 0
                        ? "\"@\" names no batch. Write @<name>, e.g. @smoke."
                        : $"\"{trimmed}\" ends in \"@\" and names no batch.");
            }

            if (name.Contains('#', StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"\"{trimmed}\" names a case of a batch. A batch already says which steps it runs.");
            }

            return owner.Length == 0
                ? new RunSelector(RunSelectorKind.Batch, name)
                : new RunSelector(RunSelectorKind.Batch, owner, Batch: name);
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
        RunSelectorKind.Batch => Batch is null ? $"@{Path}" : $"{Path}@{Batch}",
        _ => Case is null ? Path! : $"{Path}#{Case}",
    };
}
