using System.Text.Json;
using System.Xml.Linq;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Testing;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// Writing a run's results out for something else to read.
/// </summary>
public class RunReportWriterTests
{
    private static RunStep Step(int n, string folder = "/w/collections/Orders") =>
        new(n, $"r{n}", $"{folder}/r{n}.json", folder);

    private static StepReport Passed(int n, int code = 200) =>
        new(Step(n), StepStatus.Passed, code, "OK", 12, 340, [new AssertionResult(true, "status is 200", "200")], [], null);

    private static StepReport Failed(int n) =>
        new(Step(n), StepStatus.Failed, 500, "Server Error", 30, 12,
            [new AssertionResult(true, "responds", "ok"), new AssertionResult(false, "status is 200", "500")], [], null);

    private static StepReport Errored(int n) =>
        new(Step(n), StepStatus.Errored, null, null, 0, 0, [], [], "No such host is known.");

    private static RunReport Report(params StepReport[] steps) => new(steps, 1234, false, false);

    // ---- JUnit ---------------------------------------------------------------------------------

    [Fact]
    public void The_junit_document_declares_utf8_because_that_is_what_gets_written()
    {
        // XmlWriter takes its declared encoding from the writer it is given, and a plain StringWriter
        // reports UTF-16 - so this announced encoding="utf-16" on a file written as UTF-8, which strict
        // parsers reject outright. On a build agent that looks like a corrupt report rather than a
        // mislabelled one.
        var xml = JUnitRunReport.Write(Report(Passed(1)));

        Assert.StartsWith("""<?xml version="1.0" encoding="utf-8"?>""", xml);
    }

    [Fact]
    public void The_junit_document_is_well_formed()
    {
        Assert.NotNull(XDocument.Parse(JUnitRunReport.Write(Report(Passed(1), Failed(2), Errored(3)))));
    }

    [Fact]
    public void The_suite_counts_match_the_report()
    {
        var suite = XDocument.Parse(JUnitRunReport.Write(Report(Passed(1), Failed(2), Errored(3))))
            .Descendants("testsuite").Single();

        Assert.Equal("3", suite.Attribute("tests")!.Value);
        Assert.Equal("1", suite.Attribute("failures")!.Value);
        Assert.Equal("1", suite.Attribute("errors")!.Value);
    }

    [Fact]
    public void One_testcase_per_REQUEST_named_after_the_request()
    {
        // Not one per assertion: a request is the thing with a name, a duration and a URL, and a build
        // page listing "status is 200" twenty times would name none of them.
        var cases = XDocument.Parse(JUnitRunReport.Write(Report(Passed(1), Failed(2))))
            .Descendants("testcase").ToList();

        Assert.Equal(["r1", "r2"], cases.Select(c => c.Attribute("name")!.Value));
    }

    [Fact]
    public void The_folder_becomes_the_classname_so_ci_groups_them_as_the_collection_does()
    {
        var testcase = XDocument.Parse(JUnitRunReport.Write(Report(Passed(1))))
            .Descendants("testcase").Single();

        Assert.Equal("Orders", testcase.Attribute("classname")!.Value);
    }

    [Fact]
    public void A_failed_assertion_becomes_a_failure_carrying_what_was_actually_seen()
    {
        var failure = XDocument.Parse(JUnitRunReport.Write(Report(Failed(1))))
            .Descendants("failure").Single();

        Assert.Equal("1 assertion failed", failure.Attribute("message")!.Value);
        Assert.Contains("got 500", failure.Value);
        Assert.DoesNotContain("responds", failure.Value);   // the passing one is not listed
    }

    [Fact]
    public void A_transport_failure_is_an_error_not_a_failure()
    {
        // JUnit's distinction is exactly ours: a test that ran and gave the wrong answer, versus one
        // that never got to run.
        var doc = XDocument.Parse(JUnitRunReport.Write(Report(Errored(1))));

        Assert.Empty(doc.Descendants("failure"));
        Assert.Equal("No such host is known.", doc.Descendants("error").Single().Attribute("message")!.Value);
    }

    [Fact]
    public void A_skipped_step_is_marked_skipped()
    {
        var doc = XDocument.Parse(JUnitRunReport.Write(Report(StepReport.SkippedStep(Step(1)))));

        Assert.Single(doc.Descendants("skipped"));
    }

    [Fact]
    public void A_non_2xx_nobody_asserted_on_is_a_note_never_a_failure()
    {
        // The report must not contradict the exit code sitting beside it.
        var step = new StepReport(Step(1), StepStatus.Passed, 503, "Unavailable", 5, 0, [], [], null);
        var doc = XDocument.Parse(JUnitRunReport.Write(Report(step)));

        Assert.Empty(doc.Descendants("failure"));
        Assert.Empty(doc.Descendants("error"));
        Assert.Contains("503", doc.Descendants("system-out").Single().Value);
    }

    [Fact]
    public void Times_are_seconds_with_a_dot_whatever_the_agents_locale()
    {
        // A build agent set to a German locale must not produce "1,234".
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var suite = XDocument.Parse(JUnitRunReport.Write(Report(Passed(1))))
                .Descendants("testsuite").Single();

            Assert.Equal("1.234", suite.Attribute("time")!.Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    // ---- JSON ----------------------------------------------------------------------------------

    [Fact]
    public void The_json_report_carries_the_verdict_and_the_counts()
    {
        using var doc = JsonDocument.Parse(JsonRunReport.Write(Report(Passed(1), Failed(2))));
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("assertionsFailed").GetInt32());
    }

    [Fact]
    public void A_captured_VALUE_never_reaches_the_report_file()
    {
        // The headline capture is an access token, and a report file is exactly the thing that gets
        // attached to a build and kept.
        var step = new StepReport(
            Step(1), StepStatus.Passed, 200, "OK", 5, 0, [],
            [new CaptureResult(true, "token", "eyJhbGciOi-a-real-looking-token", "session", null)], null);

        var json = JsonRunReport.Write(Report(step));

        Assert.DoesNotContain("eyJhbGciOi", json);
        Assert.Contains("token", json);      // the NAME is useful and safe
        Assert.Contains("session", json);
    }

    [Fact]
    public void A_step_that_errored_reports_no_status_code_rather_than_zero()
    {
        using var doc = JsonDocument.Parse(JsonRunReport.Write(Report(Errored(1))));

        var step = doc.RootElement.GetProperty("steps")[0];
        Assert.Equal(JsonValueKind.Null, step.GetProperty("statusCode").ValueKind);
        Assert.Equal("errored", step.GetProperty("status").GetString());
    }

    [Fact]
    public void Non_ascii_names_survive_readable()
    {
        // The default encoder would turn every accented request name into \uXXXX in a file meant to be
        // read by a person.
        var step = new StepReport(
            new RunStep(1, "Søk etter ordre", "/w/collections/Søk.json", "/w/collections"),
            StepStatus.Passed, 200, "OK", 1, 0, [], [], null);

        Assert.Contains("Søk etter ordre", JsonRunReport.Write(Report(step)));
    }
}
