using Fubar.Studio.UI.Cli;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Parsing the command line, and - the part that matters most - deciding whether this invocation is a
/// command-line one at all.
/// </summary>
public class CommandLineTests
{
    // ---- Window or batch job -------------------------------------------------------------------

    [Fact]
    public void Starting_the_app_normally_is_not_headless()
    {
        // The rule that keeps this from colliding with the GUI. Anything not on the short list of flags
        // that mean nothing on screen opens a window, because turning an unrecognised argument into a
        // silent batch job is the kind of surprise nobody can debug.
        Assert.False(CommandLine.IsHeadless([]));
        Assert.False(CommandLine.IsHeadless(["--some-future-window-flag"]));
        Assert.False(CommandLine.IsHeadless(["C:\\some\\workspace"]));
    }

    [Theory]
    [InlineData("--run")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--version")]
    public void Only_flags_with_no_meaning_on_screen_are_headless(string flag)
    {
        Assert.True(CommandLine.IsHeadless([flag]));
    }

    [Fact]
    public void The_headless_flag_is_found_wherever_it_sits()
    {
        Assert.True(CommandLine.IsHeadless(["--env", "Staging", "--run", "Orders"]));
    }

    // ---- Parsing -------------------------------------------------------------------------------

    [Fact]
    public void A_bare_run_means_the_whole_workspace()
    {
        // Empty string, not null: null means "--run was never given at all", and the two lead to
        // different messages.
        Assert.Equal("", CommandLine.Parse(["--run"]).Run);
    }

    [Fact]
    public void Run_takes_the_path_after_it()
    {
        Assert.Equal("Orders", CommandLine.Parse(["--run", "Orders"]).Run);
    }

    [Fact]
    public void A_bare_run_followed_by_another_flag_stays_the_whole_workspace()
    {
        var request = CommandLine.Parse(["--run", "--env", "Staging"]);

        Assert.Equal("", request.Run);
        Assert.Equal("Staging", request.Environment);
    }

    [Fact]
    public void Everything_can_be_set_at_once()
    {
        var request = CommandLine.Parse(
            ["--run", "Orders", "-w", "/ws", "-e", "Staging", "--filter", "smoke",
             "--stop-on-failure", "--delay", "250", "--report", "out.xml", "-q"]);

        Assert.Equal("Orders", request.Run);
        Assert.Equal("/ws", request.WorkspacePath);
        Assert.Equal("Staging", request.Environment);
        Assert.Equal("smoke", request.Filter);
        Assert.True(request.StopOnFailure);
        Assert.Equal(250, request.DelayMilliseconds);
        Assert.Equal("out.xml", request.ReportPath);
        Assert.True(request.Quiet);
        Assert.Null(request.Error);
    }

    [Fact]
    public void Help_and_version_short_circuit()
    {
        Assert.True(CommandLine.Parse(["--run", "Orders", "--help"]).ShowHelp);
        Assert.True(CommandLine.Parse(["--version"]).ShowVersion);
    }

    // ---- Refusals ------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_option_is_named_rather_than_ignored()
    {
        // Silently ignoring it would run something other than what was asked for, in a script.
        Assert.Contains("--nope", CommandLine.Parse(["--run", "--nope"]).Error);
    }

    [Theory]
    [InlineData("--workspace")]
    [InlineData("--env")]
    [InlineData("--filter")]
    [InlineData("--report")]
    public void A_flag_that_needs_a_value_says_so_when_it_has_none(string flag)
    {
        Assert.NotNull(CommandLine.Parse(["--run", flag]).Error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    public void Delay_must_be_a_non_negative_number(string value)
    {
        Assert.NotNull(CommandLine.Parse(["--run", "--delay", value]).Error);
    }

    [Fact]
    public void An_unknown_report_format_lists_the_ones_that_work()
    {
        var error = CommandLine.Parse(["--run", "--report-format", "yaml"]).Error;

        Assert.Contains("json", error);
        Assert.Contains("junit", error);
    }

    [Fact]
    public void A_second_bare_path_is_a_mistake_worth_naming()
    {
        Assert.Contains("two", CommandLine.Parse(["--run", "one", "two"]).Error);
    }

    // ---- Report format -------------------------------------------------------------------------

    [Fact]
    public void An_xml_path_means_junit_without_a_second_flag()
    {
        Assert.Equal(
            RunReportFormat.JUnit,
            CommandLine.ResolveFormat(CommandLine.Parse(["--run", "--report", "results.xml"])));
    }

    [Fact]
    public void Anything_else_defaults_to_json()
    {
        // A report whose format the caller did not name is being read by something they wrote, rather
        // than by a CI system that would have wanted its own.
        Assert.Equal(
            RunReportFormat.Json,
            CommandLine.ResolveFormat(CommandLine.Parse(["--run", "--report", "results.txt"])));
    }

    [Fact]
    public void An_explicit_format_beats_the_extension()
    {
        Assert.Equal(
            RunReportFormat.Json,
            CommandLine.ResolveFormat(CommandLine.Parse(["--run", "--report", "results.xml", "--report-format", "json"])));
    }

    [Theory]
    [InlineData("junit")]
    [InlineData("junit-xml")]
    [InlineData("xml")]
    [InlineData("JUnit")]
    public void The_junit_format_answers_to_what_people_actually_type(string spelling)
    {
        Assert.Equal(
            RunReportFormat.JUnit,
            CommandLine.ResolveFormat(CommandLine.Parse(["--run", "--report-format", spelling])));
    }

    // ---- Usage ---------------------------------------------------------------------------------

    [Fact]
    public void The_usage_text_states_the_exit_codes_and_the_status_rule()
    {
        // Both are things a script author has to know BEFORE writing the script, and the status rule is
        // the one that would otherwise surprise them.
        Assert.Contains("0  every request ran", CommandLine.Usage);
        Assert.Contains("2  the run could not be attempted", CommandLine.Usage);
        Assert.Contains("A non-2xx status does NOT on its own fail the run", CommandLine.Usage);
    }
}
