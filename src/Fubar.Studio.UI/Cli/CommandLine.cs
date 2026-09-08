namespace Fubar.Studio.UI.Cli;

/// <summary>How a run's results are written to a file.</summary>
public enum RunReportFormat
{
    /// <summary>The whole report as JSON - every step, its assertions and its captures.</summary>
    Json,

    /// <summary>JUnit XML. The format every CI system already knows how to render, which is the point:
    /// a failed assertion shows up as a failed test in the build's own UI rather than as a line
    /// somewhere in a log.</summary>
    JUnit,
}

/// <summary>
/// What the command line asked for, when it asked for something the window cannot do.
///
/// <para>The rule that keeps this from colliding with the GUI is the same one Fubar Diff uses: an
/// invocation is a CLI one only when it names a flag with no meaning on screen. <c>--run</c> is the
/// only one that starts work, and opening the app with no arguments is untouched.</para>
/// </summary>
public sealed record CliRequest
{
    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    /// <summary>Why the arguments could not be used, or null when they could.</summary>
    public string? Error { get; init; }

    /// <summary>The folder or request to run. Empty string means "the whole workspace".</summary>
    public string? Run { get; init; }

    /// <summary>The workspace root. Null means "work it out from <see cref="Run"/>" by walking up to
    /// the nearest <c>fubar.json</c> - which is what makes <c>--run ./collections/Orders</c> work from
    /// inside a checkout without repeating the root.</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>Environment name or id. Null runs with none, which is a real choice rather than an
    /// omission: a collection whose URLs are literal needs no environment.</summary>
    public string? Environment { get; init; }

    public string? Filter { get; init; }

    public bool StopOnFailure { get; init; }

    /// <summary>What judges each response: nothing, or the recorded snapshot. Absent means nothing,
    /// which is what --run has always done.</summary>
    public string? Oracle { get; init; }

    /// <summary>Record snapshots instead of comparing against them. Never both: there is no snapshot
    /// in that run to update.</summary>
    public bool UpdateSnapshots { get; init; }

    /// <summary>Record one snapshot for every environment rather than for the one being run.</summary>
    public bool SharedSnapshots { get; init; }

    public int DelayMilliseconds { get; init; }

    /// <summary>Where to write a report, or null for none.</summary>
    public string? ReportPath { get; init; }

    /// <summary>Null means "work it out from the file name", so <c>--report results.xml</c> needs no
    /// second flag.</summary>
    public RunReportFormat? ReportFormat { get; init; }

    /// <summary>Say nothing; the exit code is the answer. What -q means to grep and diff.</summary>
    public bool Quiet { get; init; }

    /// <summary>
    /// Raw <c>KEY=VALUE</c> strings from <c>--var</c>. The highest-precedence variable source, and the
    /// one that makes a pipeline able to authenticate at all - a build agent has no OS keyring, so
    /// before this a Secret variable resolved to nothing and the literal <c>{{token}}</c> was sent.
    /// </summary>
    public IReadOnlyList<string> Vars { get; init; } = [];

    /// <summary>Path to a dotenv-style file of the same assignments, for more than a couple of them.</summary>
    public string? EnvFilePath { get; init; }

    /// <summary>
    /// Check every workspace file against its schema instead of running anything. Same exit contract
    /// as a run: 0 valid, 1 invalid, 2 could not tell.
    ///
    /// <para>What makes "a workspace is plain files in your repository" a workflow rather than a
    /// claim - a malformed request.json can fail a pull request instead of being found by whoever
    /// next opens it.</para>
    /// </summary>
    public bool Validate { get; init; }

    /// <summary>Treat warnings - a credential-shaped value in a committed file - as failures.</summary>
    public bool Strict { get; init; }

    /// <summary>Append one JSON line describing this run to the given file. Off unless asked for; a
    /// machine policy can also turn it on for every run.</summary>
    public string? AuditLogPath { get; init; }
}

/// <summary>Parses the arguments, and decides whether this invocation is a CLI one at all.</summary>
public static class CommandLine
{
    /// <summary>
    /// Flags that mean nothing on screen. Deliberately short: anything not on this list opens the
    /// window, because turning an unrecognised argument into a silent batch job is the kind of surprise
    /// nobody can debug.
    /// </summary>
    private static readonly string[] Headless =
        ["--run", "--validate", "--help", "-h", "--version", "--oracle", "--update-snapshots"];

    public static bool IsHeadless(string[] args) =>
        args.Any(a => Headless.Contains(a, StringComparer.OrdinalIgnoreCase));

    public static CliRequest Parse(string[] args)
    {
        var request = new CliRequest();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                    return request with { ShowHelp = true };

                case "--version":
                    return request with { ShowVersion = true };

                case "--oracle":
                    if (NextValue(args, ref i) is not { Length: > 0 } oracle)
                    {
                        return request with { Error = "--oracle needs a value: none or snapshot." };
                    }

                    if (!string.Equals(oracle, "none", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(oracle, "snapshot", StringComparison.OrdinalIgnoreCase))
                    {
                        return request with { Error = $"Unknown oracle \"{oracle}\". Use none or snapshot." };
                    }

                    request = request with { Oracle = oracle.ToLowerInvariant() };
                    break;

                case "--update-snapshots":
                    request = request with { UpdateSnapshots = true };
                    break;

                case "--shared-snapshots":
                    request = request with { SharedSnapshots = true };
                    break;

                case "--run":
                    // A bare --run is legitimate and means the whole workspace, so a missing value is
                    // only an error when the next token is another flag... which it cannot be
                    // distinguished from. Treat "no value" as the whole workspace.
                    request = request with { Run = NextValue(args, ref i) ?? "" };
                    break;

                case "--workspace":
                case "-w":
                    if (NextValue(args, ref i) is not { } workspace)
                    {
                        return request with { Error = "--workspace needs a path." };
                    }

                    request = request with { WorkspacePath = workspace };
                    break;

                case "--env":
                case "-e":
                    if (NextValue(args, ref i) is not { } environment)
                    {
                        return request with { Error = "--env needs an environment name." };
                    }

                    request = request with { Environment = environment };
                    break;

                case "--filter":
                    if (NextValue(args, ref i) is not { } filter)
                    {
                        return request with { Error = "--filter needs some text to match." };
                    }

                    request = request with { Filter = filter };
                    break;

                case "--stop-on-failure":
                    request = request with { StopOnFailure = true };
                    break;

                case "--delay":
                    if (NextValue(args, ref i) is not { } delayText || !int.TryParse(delayText, out var delay) || delay < 0)
                    {
                        return request with { Error = "--delay needs a number of milliseconds (0 or more)." };
                    }

                    request = request with { DelayMilliseconds = delay };
                    break;

                case "--report":
                    if (NextValue(args, ref i) is not { } reportPath)
                    {
                        return request with { Error = "--report needs a file path." };
                    }

                    request = request with { ReportPath = reportPath };
                    break;

                case "--report-format":
                    if (NextValue(args, ref i) is not { } formatText)
                    {
                        return request with { Error = "--report-format needs a format (json or junit)." };
                    }

                    if (ParseFormat(formatText) is not { } format)
                    {
                        return request with { Error = $"Unknown report format \"{formatText}\". Use json or junit." };
                    }

                    request = request with { ReportFormat = format };
                    break;

                case "--var":
                    if (NextValue(args, ref i) is not { } assignment)
                    {
                        return request with { Error = "--var needs KEY=VALUE." };
                    }

                    // Refused rather than ignored: a malformed --var is a credential the caller
                    // believes they passed, and running without it produces a 401 that points at the
                    // API instead of at the typo.
                    if (Fubar.Studio.Core.Variables.VariableAssignment.DescribeInvalid(assignment) is { } invalid)
                    {
                        return request with { Error = $"--var: {invalid}" };
                    }

                    request = request with { Vars = [.. request.Vars, assignment] };
                    break;

                case "--env-file":
                    if (NextValue(args, ref i) is not { } envFile)
                    {
                        return request with { Error = "--env-file needs a path." };
                    }

                    request = request with { EnvFilePath = envFile };
                    break;

                case "--validate":
                    request = request with { Validate = true };
                    break;

                case "--strict":
                    request = request with { Strict = true };
                    break;

                case "--audit-log":
                    if (NextValue(args, ref i) is not { } auditPath)
                    {
                        return request with { Error = "--audit-log needs a path." };
                    }

                    request = request with { AuditLogPath = auditPath };
                    break;

                case "--quiet":
                case "-q":
                    request = request with { Quiet = true };
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        return request with { Error = $"Unknown option \"{arg}\"." };
                    }

                    // A bare path after --run is that run's target; anywhere else it is a mistake worth
                    // naming rather than silently ignoring.
                    if (request.Run is "")
                    {
                        request = request with { Run = arg };
                        break;
                    }

                    return request with { Error = $"Unexpected argument \"{arg}\"." };
            }
        }

        // Recording is not comparing. Accepting both would have to pick one silently, and either
        // choice surprises somebody - so it is refused with the reason.
        if (request.UpdateSnapshots && string.Equals(request.Oracle, "snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return request with { Error = "--update-snapshots records snapshots; --oracle snapshot compares against them. Use one." };
        }

        return request;
    }

    /// <summary>
    /// The format for a report, from the flag or - failing that - from the file's extension. Defaults
    /// to JSON, because a report the caller did not name a format for is being read by something they
    /// wrote rather than by a CI system that would have wanted its own.
    /// </summary>
    public static RunReportFormat ResolveFormat(CliRequest request) =>
        request.ReportFormat
        ?? (Path.GetExtension(request.ReportPath ?? "").ToLowerInvariant() switch
        {
            ".xml" => RunReportFormat.JUnit,
            _ => RunReportFormat.Json,
        });

    private static RunReportFormat? ParseFormat(string text) => text.ToLowerInvariant() switch
    {
        "json" => RunReportFormat.Json,
        "junit" or "junit-xml" or "xml" => RunReportFormat.JUnit,
        _ => null,
    };

    /// <summary>The next argument, unless it is another flag (or there is none).</summary>
    private static string? NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            return null;
        }

        return args[++index];
    }

    public const string Usage = """
        FubarAPIStudio - run a collection from the command line.

        Usage:
          FubarAPIStudio --run [<folder-or-request>] [options]

        The path is relative to the workspace's collections/ directory, or absolute.
        Omit it to run the whole workspace.

        Options:
          -w, --workspace <path>   Workspace root. Defaults to walking up from the run
                                   target (or the working directory) to the nearest fubar.json.
          -e, --env <name>         Environment to run against, by name or id.
              --var KEY=VALUE      Supply a variable. Repeatable. Beats everything else,
                                   including the environment file and the OS keyring.
              --env-file <path>    Read KEY=VALUE lines from a file. Same idea, for more
                                   than a couple of them.
              --filter <text>      Only run requests whose name contains this text.
              --oracle <what>      Compare each response against: none (default) or snapshot.
                                   A missing snapshot is reported and fails the run - it is
                                   never treated as a pass.
              --update-snapshots   Record snapshots instead of comparing against them.
              --shared-snapshots   With --update-snapshots, record one snapshot for every
                                   environment rather than for the one being run.
              --stop-on-failure    Stop at the first failed or errored request.
              --delay <ms>         Wait this long between requests.
              --report <path>      Write a report.
              --report-format <f>  json (default) or junit. Inferred from the path's
                                   extension when not given.
              --validate           Check every workspace file against its schema and exit.
              --strict             With --validate, treat warnings as failures.
              --audit-log <path>   Append one JSON line per run: who, where, and the verdict.
                                   Never a captured value or a body.
          -q, --quiet              Print nothing; the exit code is the answer.
          -h, --help               Show this.
              --version            Show the version.

        Exit codes:
          0  every request ran and every assertion passed
          1  something failed: an assertion, or a request that got no response
          2  the run could not be attempted at all

        A non-2xx status does NOT on its own fail the run - only an assertion does.
        Assert on the status when you want it enforced. A cancelled or empty run
        exits 1 rather than 0, so a filter that matches nothing cannot pass by default.

        Variables, highest precedence first:
          --var KEY=VALUE
          --env-file
          FUBAR_VAR_<KEY> in the process environment
          the active environment's session values
          the OS keyring (secret variables)
          the environment file

        {{api_key}} is fed by FUBAR_VAR_API_KEY: the name is upper-cased and anything
        that is not a letter or digit becomes an underscore, which is the only shape
        some CI systems allow. Values supplied this way are never logged and never
        written to an environment file.

        A build agent has no OS keyring, so this is how a pipeline supplies a secret.
        An unresolved {{variable}} stops the run with exit code 2 rather than sending
        the literal text to the server.

        Examples:
          FubarAPIStudio --run --env Staging --report results.xml
          FubarAPIStudio --run Orders --stop-on-failure
          FubarAPIStudio --run -w ./api-tests --filter smoke -q
          FubarAPIStudio --run --env CI --var api_key="$API_KEY" --report results.xml
          FubarAPIStudio --validate -w ./api-tests
        """;
}
