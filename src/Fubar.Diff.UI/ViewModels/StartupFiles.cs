namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// What was named on the command line: two files to compare, or three to merge. Registered in DI so
/// the shell receives them like any other dependency rather than reading
/// <c>Environment.GetCommandLineArgs()</c> itself, which would make it untestable.
/// </summary>
/// <param name="Left">Left-hand path, or null when not supplied.</param>
/// <param name="Right">Right-hand path, or null when not supplied.</param>
/// <param name="Base">
/// The common ancestor, when a three-way merge was asked for. Null for an ordinary comparison, which
/// is what distinguishes the two modes - see <see cref="IsMerge"/>.
/// </param>
public sealed record StartupFiles(string? Left, string? Right, string? Base = null)
{
    /// <summary>Nothing on the command line - the app opens empty.</summary>
    public static StartupFiles None { get; } = new(null, null);

    /// <summary>
    /// Reads the command line.
    ///
    /// <c>FubarDiff left right</c> compares two files. <c>FubarDiff --merge base local remote</c> opens
    /// a three-way merge, in the argument order <c>git mergetool</c> uses for <c>$BASE $LOCAL
    /// $REMOTE</c> - the one place these arguments come from that has a settled convention, and there
    /// is no reason to invent a second one.
    ///
    /// Anything extra is ignored rather than treated as an error: refusing to start over a stray
    /// argument would be worse than quietly opening the files that were meant.
    /// </summary>
    public static StartupFiles FromArgs(string[] args)
    {
        if (args.Length > 0 && IsMergeFlag(args[0]))
        {
            // args are BASE, LOCAL, REMOTE. LOCAL is "mine" and lands on the RIGHT, matching the
            // two-way window's convention that the right-hand side is the one being merged INTO;
            // REMOTE is "theirs" and lands on the left. Written out positionally rather than passed
            // through in argument order, because the two orders are NOT the same and the difference is
            // invisible in any test whose left and right files are interchangeable.
            return args.Length >= 4
                ? new StartupFiles(Left: args[3], Right: args[2], Base: args[1])
                : None;
        }

        return args.Length switch
        {
            0 => None,
            1 => new StartupFiles(args[0], null),
            _ => new StartupFiles(args[0], args[1]),
        };
    }

    private static bool IsMergeFlag(string arg) =>
        arg is "--merge" or "-m" or "/merge";

    /// <summary>True when both sides were supplied, so a comparison can run at startup.</summary>
    public bool HasBoth => !string.IsNullOrWhiteSpace(Left) && !string.IsNullOrWhiteSpace(Right);

    /// <summary>True when all three were supplied, so a merge can run at startup instead.</summary>
    public bool IsMerge => HasBoth && !string.IsNullOrWhiteSpace(Base);
}
