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

    // ---- Oracles and snapshots -------------------------------------------------------------------

    [Fact]
    public void No_oracle_is_the_default_which_is_what_run_has_always_done()
    {
        Assert.Null(CommandLine.Parse(["--run"]).Oracle);
    }

    [Fact]
    public void The_snapshot_oracle_is_selected_by_name()
    {
        Assert.Equal(
            new CliOracle(CliOracleKind.Snapshot),
            CommandLine.Parse(["--run", "--oracle", "snapshot"]).Oracle);
    }

    [Theory]
    [InlineData("env:Production")]
    [InlineData("environment:Production")]
    public void The_environment_oracle_carries_the_environment_it_compares_against(string value)
    {
        Assert.Equal(
            new CliOracle(CliOracleKind.Environment, "Production"),
            CommandLine.Parse(["--run", "--oracle", value]).Oracle);
    }

    /// <summary>A comparison against nothing is not a comparison, and defaulting to some environment
    /// would compare against one nobody named.</summary>
    [Fact]
    public void An_environment_oracle_with_no_environment_is_refused()
    {
        Assert.NotNull(CommandLine.Parse(["--run", "--oracle", "env:"]).Error);
    }

    /// <summary>There is no snapshot in a two-sided run to update, so this asks for something that
    /// does not exist rather than for one of two readings.</summary>
    [Fact]
    public void Recording_during_an_environment_comparison_is_refused()
    {
        Assert.NotNull(
            CommandLine.Parse(["--run", "--oracle", "env:Production", "--update-snapshots"]).Error);
    }

    // ---- The run verb and its selectors ----------------------------------------------------------

    [Fact]
    public void The_run_verb_means_the_whole_workspace()
    {
        var request = CommandLine.Parse(["run"]);

        Assert.Null(request.Error);
        Assert.Equal("", request.Run);
    }

    [Fact]
    public void The_run_verb_takes_a_selector()
    {
        Assert.Equal("orders/get-order#default", CommandLine.Parse(["run", "orders/get-order#default"]).Run);
        Assert.Equal("@smoke", CommandLine.Parse(["run", "@smoke"]).Run);
    }

    [Fact]
    public void The_run_verb_chooses_the_headless_path()
    {
        Assert.True(CommandLine.IsHeadless(["run"]));
        Assert.True(CommandLine.IsHeadless(["run", "@smoke"]));
    }

    /// <summary>Anywhere but first, "run" is an ordinary folder name - and turning one into a batch
    /// job with no window is the surprise the headless list exists to avoid.</summary>
    [Fact]
    public void A_folder_called_run_does_not_make_an_invocation_headless()
    {
        Assert.False(CommandLine.IsHeadless(["--merge", "run"]));
    }

    [Fact]
    public void An_unknown_oracle_is_refused_by_name_rather_than_ignored()
    {
        var request = CommandLine.Parse(["--run", "--oracle", "guesswork"]);

        Assert.Contains("guesswork", request.Error!, StringComparison.Ordinal);
    }

    /// <summary>Recording and comparing are opposite acts. Accepting both would have to pick one
    /// silently, and either choice surprises somebody.</summary>
    [Fact]
    public void Recording_and_comparing_at_once_is_refused()
    {
        var request = CommandLine.Parse(["--run", "--oracle", "snapshot", "--update-snapshots"]);

        Assert.NotNull(request.Error);
    }

    [Fact]
    public void Recording_alone_is_fine_and_defaults_to_per_environment()
    {
        var request = CommandLine.Parse(["--run", "--update-snapshots"]);

        Assert.Null(request.Error);
        Assert.True(request.UpdateSnapshots);
        Assert.False(request.SharedSnapshots);
    }

    /// <summary>A run that must exit with a status code cannot also be showing a window, so the flags
    /// that mean "batch" have to be recognised before Avalonia is configured.</summary>
    [Fact]
    public void The_new_flags_choose_the_headless_path()
    {
        Assert.True(CommandLine.IsHeadless(["--oracle", "snapshot"]));
        Assert.True(CommandLine.IsHeadless(["--update-snapshots"]));
    }
}
