using System;
using System.Collections.Generic;
using Fubar.Diff.Application.Reporting;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.UI.Cli;

/// <summary>
/// What the command line asked for, when it asked for something the window cannot do.
///
/// The rule that keeps this from colliding with the GUI: an invocation is a CLI one only if it names
/// a flag that has no meaning on screen (--check, --report, --help, --version). <c>FubarDiff a b</c>
/// still opens a window, and so does <c>--merge</c>, because those are what a difftool or mergetool
/// configuration passes and quietly turning one of them into a batch job would be a surprise nobody
/// could debug.
/// </summary>
public sealed record CliRequest
{
    /// <summary>Print usage and exit. Set by --help, and by any parse error worth explaining.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>Print the version and exit.</summary>
    public bool ShowVersion { get; init; }

    /// <summary>Why the arguments could not be used, or null when they could.</summary>
    public string? Error { get; init; }

    public string? Left { get; init; }

    public string? Right { get; init; }

    /// <summary>Where to write a report, or null for none.</summary>
    public string? ReportPath { get; init; }

    /// <summary>
    /// The report's format. Null means "work it out from the file name", which covers every ordinary
    /// invocation - <c>--report out.html</c> needs no second flag.
    /// </summary>
    public ReportFormat? ReportFormat { get; init; }

    /// <summary>Say nothing at all; the exit code is the answer. What -q means to grep and diff.</summary>
    public bool Quiet { get; init; }

    /// <summary>Unchanged lines to keep either side of each change in the report.</summary>
    public int ContextLines { get; init; } = 3;

    /// <summary>The comparison options the flags add up to.</summary>
    public ComparisonOptions Options { get; init; } = new();
}

/// <summary>
/// Parses the command line into a <see cref="CliRequest"/>, or decides this is a GUI invocation.
///
/// Deliberately hand-written rather than pulled from a package: the surface is a dozen flags, the
/// project takes no dependency it does not need, and the one rule that actually matters here - which
/// invocations are NOT for the CLI - is a policy decision rather than a parsing one.
/// </summary>
public static class CommandLine
{
    /// <summary>Flags that mean "do not open a window".</summary>
    private static readonly string[] Headless =
    [
        "--check", "--quiet", "-q", "--report", "--report-format", "--help", "-h", "-?", "/?", "--version",
    ];

    /// <summary>
    /// True when these arguments are for the command line rather than the window.
    ///
    /// Checked before anything is parsed, and before Avalonia starts, because the two paths cannot
    /// both own the process: a headless run has to be able to exit with a status code rather than
    /// showing a window nobody asked for.
    /// </summary>
    public static bool IsHeadless(string[] args)
    {
        foreach (var arg in args)
        {
            foreach (var flag in Headless)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static CliRequest Parse(string[] args)
    {
        var request = new CliRequest();
        var options = new ComparisonOptions();
        var json = options.Json;
        var code = options.Code;
        var files = new List<string>();
        var ignoredPaths = new List<string>(json.IgnoredPaths);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "--help" or "-h" or "-?" or "/?":
                    return request with { ShowHelp = true };

                case "--version":
                    return request with { ShowVersion = true };

                // Both run headless; only one of them says anything. -q is grep's meaning of quiet -
                // the exit code is the whole answer - and --check is the same run with the summary
                // line a build log wants.
                case "--check":
                    break;

                case "--quiet" or "-q":
                    request = request with { Quiet = true };
                    break;

                case "--report":
                    if (Next(args, ref i) is not { } reportPath)
                    {
                        return Fail("--report needs a file to write to.");
                    }

                    request = request with { ReportPath = reportPath };
                    break;

                case "--report-format":
                    if (Next(args, ref i) is not { } formatName)
                    {
                        return Fail("--report-format needs one of: text, html, json, patch.");
                    }

                    if (!Enum.TryParse<ReportFormat>(formatName, ignoreCase: true, out var format))
                    {
                        return Fail($"'{formatName}' is not a report format. Use text, html, json or patch.");
                    }

                    request = request with { ReportFormat = format };
                    break;

                case "--context" or "-c":
                    if (Next(args, ref i) is not { } contextText || !int.TryParse(contextText, out var context) || context < 0)
                    {
                        return Fail("--context needs a number of lines, 0 or more.");
                    }

                    request = request with { ContextLines = context };
                    break;

                case "--mode":
                    if (Next(args, ref i) is not { } modeName)
                    {
                        return Fail("--mode needs one of: auto, text, json, yaml.");
                    }

                    if (!Enum.TryParse<ComparisonMode>(modeName, ignoreCase: true, out var mode))
                    {
                        return Fail($"'{modeName}' is not a comparison mode. Use auto, text, json or yaml.");
                    }

                    options = options with { Mode = mode };
                    break;

                case "--ignore-whitespace" or "-w":
                    options = options with { IgnoreWhitespace = true };
                    break;

                case "--ignore-case" or "-i":
                    options = options with { IgnoreCase = true };
                    break;

                case "--ignore-comments":
                    code = code with { IgnoreComments = true };
                    break;

                case "--ignore-blank-lines":
                    code = code with { IgnoreBlankLines = true };
                    break;

                case "--ignore-path":
                    if (Next(args, ref i) is not { } path)
                    {
                        return Fail("--ignore-path needs a JSON path, e.g. $.requestId.");
                    }

                    ignoredPaths.Add(path);
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        return Fail($"Unknown option '{arg}'.");
                    }

                    files.Add(arg);
                    break;
            }
        }

        if (files.Count != 2)
        {
            return Fail(files.Count < 2
                ? "Two files are needed to compare."
                : $"Expected two files, got {files.Count}.");
        }

        return request with
        {
            Left = files[0],
            Right = files[1],
            Options = options with
            {
                Code = code,
                Json = json with { IgnoredPaths = ignoredPaths },
            },
        };
    }

    /// <summary>The next argument, consuming it, or null when the flag was last on the line.</summary>
    private static string? Next(string[] args, ref int index) =>
        index + 1 < args.Length ? args[++index] : null;

    private static CliRequest Fail(string error) => new() { Error = error, ShowHelp = true };

    /// <summary>
    /// Usage. Kept short enough to read at the point of failure - the flags that need explaining
    /// explain themselves, and the ones that do not are listed without ceremony.
    /// </summary>
    public const string Usage = """
        Fubar Diff - compare two files.

        Windowed:
          FubarDiff <left> <right>              open a comparison
          FubarDiff --merge <base> <local> <remote>
                                                open a three-way merge (git mergetool order)

        Headless:
          FubarDiff --check <left> <right>      compare and exit; prints one line
          FubarDiff --report <file> <left> <right>
                                                write a report; format from the extension

        Exit codes:
          0  the files are the same
          1  they differ
          2  something went wrong (a file could not be read)

        Options:
          --report-format text|html|json|patch  override the format the extension implies
          --context, -c <n>                     unchanged lines to keep around each change (default 3)
          --mode auto|text|json|yaml            how to compare; auto reads JSON as structure, and
                                                anything named .yaml or .yml as YAML
          --ignore-whitespace, -w               leading and trailing whitespace stops counting
          --ignore-case, -i
          --ignore-comments                     code files only
          --ignore-blank-lines                  code files only
          --ignore-path <path>                  never report this JSON path; repeatable
          --check                               print the summary line (the default headless mode)
          --quiet, -q                           print nothing; the exit code is the answer
          --help, --version

        Examples:
          FubarDiff --check old.json new.json --ignore-path '$.timestamp'
          FubarDiff --report diff.html src/a.cs src/b.cs
          FubarDiff --report - --report-format patch a.txt b.txt > changes.patch
        """;
}
