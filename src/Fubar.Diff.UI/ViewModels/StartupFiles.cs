namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// The two files named on the command line, if any: <c>FubarDiff left.txt right.txt</c>. Registered in
/// DI so <see cref="MainViewModel"/> receives them like any other dependency rather than reading
/// <c>Environment.GetCommandLineArgs()</c> itself, which would make it untestable.
/// </summary>
/// <param name="Left">Left-hand path, or null when not supplied.</param>
/// <param name="Right">Right-hand path, or null when not supplied.</param>
public sealed record StartupFiles(string? Left, string? Right)
{
    /// <summary>Nothing on the command line - the app opens empty.</summary>
    public static StartupFiles None { get; } = new(null, null);

    /// <summary>
    /// Reads the first two positional arguments. Anything beyond two is ignored rather than treated
    /// as an error: a diff of exactly two things is the whole premise, and refusing to start over a
    /// stray argument would be worse than quietly comparing the two that were meant.
    /// </summary>
    public static StartupFiles FromArgs(string[] args) => args.Length switch
    {
        0 => None,
        1 => new StartupFiles(args[0], null),
        _ => new StartupFiles(args[0], args[1]),
    };

    /// <summary>True when both sides were supplied, so a comparison can run at startup.</summary>
    public bool HasBoth => !string.IsNullOrWhiteSpace(Left) && !string.IsNullOrWhiteSpace(Right);
}
